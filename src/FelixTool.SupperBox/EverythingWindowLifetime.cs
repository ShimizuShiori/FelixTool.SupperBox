using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace FelixTool.SupperBox;

internal readonly record struct EverythingSearchWindow(nint Handle, string Title);

internal interface IEverythingWindows
{
    IReadOnlyList<EverythingSearchWindow> GetSearchWindows();
    void Mark(nint handle, string marker);
    void CloseMarkedWindow(nint handle, string marker);
}

internal sealed class EverythingWindowLifetime
{
    internal static readonly TimeSpan AutoCloseDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMilliseconds(100);
    private readonly IEverythingWindows _windows;
    private readonly Action<ProcessStartInfo> _start;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly Action<Exception> _reportError;

    public EverythingWindowLifetime() : this(
        new EverythingWindows(),
        info =>
        {
            using var process = Process.Start(info);
        },
        duration => Task.Delay(duration),
        exception => System.Windows.MessageBox.Show(
            $"Everything 窗口自动关闭失败：{exception.GetBaseException().Message}",
            "SupperBox",
            MessageBoxButton.OK,
            MessageBoxImage.Error))
    {
    }

    internal EverythingWindowLifetime(
        IEverythingWindows windows,
        Action<ProcessStartInfo> start,
        Func<TimeSpan, Task> delay,
        Action<Exception> reportError)
    {
        _windows = windows;
        _start = start;
        _delay = delay;
        _reportError = reportError;
    }

    public void Open(ProcessStartInfo startInfo, string repositoryPath)
    {
        var existingWindows = _windows.GetSearchWindows().Select(window => window.Handle).ToHashSet();
        _start(startInfo);
        _ = CloseAfterDelayAsync(existingWindows, repositoryPath);
    }

    private async Task CloseAfterDelayAsync(HashSet<nint> existingWindows, string repositoryPath)
    {
        try
        {
            var searchPrefix = $"\"{Path.TrimEndingDirectorySeparator(repositoryPath)}\\\"";
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var matches = _windows.GetSearchWindows()
                    .Where(window => !existingWindows.Contains(window.Handle)
                        && window.Title.StartsWith(searchPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count > 1)
                {
                    throw new InvalidOperationException("发现多个新建的同目录搜索窗口，无法安全确定应关闭的窗口。");
                }

                if (matches.Count == 1)
                {
                    var marker = $"SupperBox.AutoClose.{Guid.NewGuid():N}";
                    var handle = matches[0].Handle;
                    _windows.Mark(handle, marker);
                    await _delay(AutoCloseDelay);
                    _windows.CloseMarkedWindow(handle, marker);
                    return;
                }

                await _delay(DiscoveryInterval);
            }

            throw new TimeoutException("未能在 10 秒内找到新建的 Everything 搜索窗口。");
        }
        catch (Exception exception)
        {
            _reportError(exception);
        }
    }
}

internal sealed class EverythingWindows : IEverythingWindows
{
    public IReadOnlyList<EverythingSearchWindow> GetSearchWindows()
    {
        var windows = new List<EverythingSearchWindow>();
        if (!EnumWindows((handle, _) =>
        {
            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "EVERYTHING"
                || className.ToString().StartsWith("EVERYTHING_(", StringComparison.Ordinal))
            {
                var title = new StringBuilder(32768);
                GetWindowText(handle, title, title.Capacity);
                windows.Add(new EverythingSearchWindow(handle, title.ToString()));
            }

            return true;
        }, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return windows;
    }

    public void Mark(nint handle, string marker)
    {
        if (!SetProp(handle, marker, 1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void CloseMarkedWindow(nint handle, string marker)
    {
        // A closed window's handle may have been reused for an unrelated window.
        if (GetProp(handle, marker) != 1)
        {
            return;
        }

        if (!PostMessage(handle, 0x0010, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        RemoveProp(handle, marker);
    }

    private delegate bool EnumWindowCallback(nint handle, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProp(nint handle, string name, nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetProp(nint handle, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint RemoveProp(nint handle, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);
}
