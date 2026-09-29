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
            ShowOnUiThread(content, cancellationToken);
            return;
        }

        await owner.Dispatcher.InvokeAsync(
            () => ShowOnUiThread(content, cancellationToken));
    }

    private void ShowOnUiThread(string content, CancellationToken cancellationToken)
    {
        var window = new OutputWindow(content);
        var registration = cancellationToken.Register(
            () => window.Dispatcher.BeginInvoke(window.Close));

        window.Closed += (_, _) => registration.Dispose();
        window.Show();
        owner.Hide();
    }
}
