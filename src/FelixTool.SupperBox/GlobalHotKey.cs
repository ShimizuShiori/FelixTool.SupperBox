using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FelixTool.SupperBox;

public sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0x5342;
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint VkF1 = 0x70;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;

    public GlobalHotKey(System.Windows.Window window, Action activated)
    {
        _windowHandle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("无法获取窗口句柄。");
        _source.AddHook(ProcessMessage);
        Activated = activated;

        if (!RegisterHotKey(_windowHandle, HotKeyId, ModControl, VkF1))
        {
            _source.RemoveHook(ProcessMessage);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册 Ctrl+F1，快捷键可能已被其他程序占用。");
        }
    }

    private Action Activated { get; }

    public void Dispose()
    {
        UnregisterHotKey(_windowHandle, HotKeyId);
        _source.RemoveHook(ProcessMessage);
    }

    private nint ProcessMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam == HotKeyId)
        {
            handled = true;
            Activated();
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
