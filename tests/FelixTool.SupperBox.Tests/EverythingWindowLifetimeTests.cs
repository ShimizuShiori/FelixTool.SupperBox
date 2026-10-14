using System.Diagnostics;

namespace FelixTool.SupperBox.Tests;

public sealed class EverythingWindowLifetimeTests
{
    private const string RepositoryPath = @"C:\Repositories\Alpha";
    private const string WindowTitle = "\"C:\\Repositories\\Alpha\\\" - Everything";

    [Fact]
    public async Task Open_ClosesOnlyNewWindowAfterExactlyFiveMinutes()
    {
        var windows = new RecordingWindows();
        windows.Windows.Add(new(1, WindowTitle));
        var elapsed = new TaskCompletionSource();
        var closed = new TaskCompletionSource();
        var delays = new List<TimeSpan>();
        var errors = new List<Exception>();
        windows.OnClose = () => closed.SetResult();
        var lifetime = new EverythingWindowLifetime(
            windows,
            _ => windows.Windows.Add(new(2, WindowTitle)),
            duration =>
            {
                delays.Add(duration);
                return elapsed.Task;
            },
            errors.Add);

        lifetime.Open(new ProcessStartInfo(), RepositoryPath);

        Assert.Equal([TimeSpan.FromMinutes(5)], delays);
        Assert.Equal((nint)2, windows.MarkedHandle);
        Assert.Null(windows.ClosedHandle);

        windows.Windows[1] = new(2, "Changed search - Everything");
        elapsed.SetResult();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((nint)2, windows.ClosedHandle);
        Assert.Equal(windows.MarkedMarker, windows.ClosedMarker);
        Assert.Empty(errors);
    }

    [Fact]
    public void Open_WaitsForWindowCreationAndIgnoresOtherRepositories()
    {
        var windows = new RecordingWindows();
        var delays = new List<TimeSpan>();
        var errors = new List<Exception>();
        var lifetime = new EverythingWindowLifetime(
            windows,
            _ => windows.Windows.Add(new(1, "\"C:\\Repositories\\Beta\\\" - Everything")),
            duration =>
            {
                delays.Add(duration);
                if (duration < TimeSpan.FromMinutes(5))
                {
                    windows.Windows.Add(new(2, WindowTitle));
                    return Task.CompletedTask;
                }

                return new TaskCompletionSource().Task;
            },
            errors.Add);

        lifetime.Open(new ProcessStartInfo(), RepositoryPath);

        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5)], delays);
        Assert.Equal((nint)2, windows.MarkedHandle);
        Assert.Empty(errors);
    }

    [Fact]
    public void Open_ReportsDiscoveryTimeoutWithoutClosingExistingWindow()
    {
        var windows = new RecordingWindows();
        windows.Windows.Add(new(1, WindowTitle));
        var errors = new List<Exception>();
        var delays = new List<TimeSpan>();
        var lifetime = new EverythingWindowLifetime(
            windows, _ => { },
            duration =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            },
            errors.Add);

        lifetime.Open(new ProcessStartInfo(), RepositoryPath);

        Assert.IsType<TimeoutException>(Assert.Single(errors));
        Assert.Equal(TimeSpan.FromSeconds(10), TimeSpan.FromTicks(delays.Sum(delay => delay.Ticks)));
        Assert.Null(windows.MarkedHandle);
        Assert.Null(windows.ClosedHandle);
    }

    [Fact]
    public void Open_ReportsAmbiguousWindowsWithoutClosingEither()
    {
        var windows = new RecordingWindows();
        var errors = new List<Exception>();
        var lifetime = new EverythingWindowLifetime(
            windows,
            _ => windows.Windows.AddRange([new(1, WindowTitle), new(2, WindowTitle)]),
            _ => Task.CompletedTask,
            errors.Add);

        lifetime.Open(new ProcessStartInfo(), RepositoryPath);

        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Null(windows.MarkedHandle);
        Assert.Null(windows.ClosedHandle);
    }

    [Fact]
    public void Open_ReportsCloseFailure()
    {
        var windows = new RecordingWindows
        {
            OnClose = () => throw new InvalidOperationException("Close failed")
        };
        var errors = new List<Exception>();
        var lifetime = new EverythingWindowLifetime(
            windows,
            _ => windows.Windows.Add(new(1, WindowTitle)),
            _ => Task.CompletedTask,
            errors.Add);

        lifetime.Open(new ProcessStartInfo(), RepositoryPath);

        Assert.Equal("Close failed", Assert.Single(errors).Message);
    }

    private sealed class RecordingWindows : IEverythingWindows
    {
        public List<EverythingSearchWindow> Windows { get; } = [];
        public nint? MarkedHandle { get; private set; }
        public string? MarkedMarker { get; private set; }
        public nint? ClosedHandle { get; private set; }
        public string? ClosedMarker { get; private set; }
        public Action? OnClose { get; set; }

        public IReadOnlyList<EverythingSearchWindow> GetSearchWindows() => Windows.ToList();

        public void Mark(nint handle, string marker)
        {
            MarkedHandle = handle;
            MarkedMarker = marker;
        }

        public void CloseMarkedWindow(nint handle, string marker)
        {
            ClosedHandle = handle;
            ClosedMarker = marker;
            OnClose?.Invoke();
        }
    }
}
