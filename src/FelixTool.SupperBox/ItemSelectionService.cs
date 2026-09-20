using System.Windows;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public sealed record PluginContext(IItemSelectionService ItemSelection) : IPluginContext;

public sealed class ItemSelectionService(Window owner) : IItemSelectionService
{
    public async Task<T> SelectAsync<T>(
        string title,
        IReadOnlyList<SelectionItem<T>> items,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("至少需要一个可选项。", nameof(items));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (owner.Dispatcher.CheckAccess())
        {
            return Select(owner, title, items, cancellationToken);
        }

        return await owner.Dispatcher.InvokeAsync(
            () => Select(owner, title, items, cancellationToken));
    }

    private static T Select<T>(
        Window owner,
        string title,
        IReadOnlyList<SelectionItem<T>> items,
        CancellationToken cancellationToken)
    {
        var entries = items
            .Select(item => new ItemSelectionEntry(item.Value!, item.Label, item.Description))
            .ToList();
        var window = new ItemSelectionWindow(title, entries)
        {
            Owner = owner
        };
        using var registration = cancellationToken.Register(
            () => window.Dispatcher.BeginInvoke(window.Cancel));

        if (window.ShowDialog() != true || window.SelectedValue is not T selected)
        {
            throw new OperationCanceledException("未选择任何条目。", cancellationToken);
        }

        return selected;
    }
}

public sealed record ItemSelectionEntry(object Value, string Label, string Description);
