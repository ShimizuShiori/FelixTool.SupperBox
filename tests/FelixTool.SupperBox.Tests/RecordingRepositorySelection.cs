using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

internal sealed class RecordingRepositorySelection : IItemSelectionService
{
    public int SelectedIndex { get; init; }
    public bool Cancel { get; init; }
    public int CallCount { get; private set; }
    public IReadOnlyList<SelectionItem<string>> Items { get; private set; } = [];

    public Task<T> SelectAsync<T>(
        string title,
        IReadOnlyList<SelectionItem<T>> items,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Items = items.Select(item => new SelectionItem<string>(
            item.Value!.ToString()!, item.Label, item.Description)).ToList();
        if (Cancel)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return Task.FromResult(items[SelectedIndex].Value);
    }
}
