using System.Windows;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public sealed class OutputService(Window owner) : IOutputService
{
    public async Task ShowAsync(string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        if (owner.Dispatcher.CheckAccess())
        {
            await ShowOnUiThreadAsync(content, cancellationToken);
            return;
        }

        var task = await owner.Dispatcher.InvokeAsync(
            () => ShowOnUiThreadAsync(content, cancellationToken));
        await task;
    }

    private Task ShowOnUiThreadAsync(string content, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new OutputWindow(content)
        {
            Owner = owner
        };
        var registration = cancellationToken.Register(
            () => window.Dispatcher.BeginInvoke(window.Close));

        window.Closed += (_, _) =>
        {
            registration.Dispose();
            completion.TrySetResult();
        };
        window.Show();
        return completion.Task;
    }
}
