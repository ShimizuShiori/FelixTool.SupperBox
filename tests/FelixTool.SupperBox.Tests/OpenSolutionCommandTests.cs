using System.IO;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class OpenSolutionCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"SupperBoxSolutions-{Guid.NewGuid():N}");

    public OpenSolutionCommandTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    [Fact]
    public void FindSolutions_SearchesFromFileDirectoryThroughParents()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "src", "feature")).FullName;
        var sourceFile = Path.Combine(child, "Sample.cs");
        File.WriteAllText(sourceFile, "");
        var rootSolution = Path.Combine(_root, "Root.sln");
        var childSolution = Path.Combine(child, "Feature.slnx");
        File.WriteAllText(rootSolution, "");
        File.WriteAllText(childSolution, "");

        var solutions = OpenSolutionCommand.FindSolutions(sourceFile);

        Assert.Equal([childSolution, rootSolution], solutions.Where(IsTestSolution));
    }

    [Fact]
    public async Task ExecuteAsync_OpensTheOnlySolutionWithoutShowingSelector()
    {
        var solution = Path.Combine(_root, "Only.sln");
        File.WriteAllText(solution, "");
        var selector = new RecordingItemSelection();
        var launcher = new RecordingSolutionLauncher();
        var command = new OpenSolutionCommand(
            selector,
            new StubVisualStudioLocator(@"C:\VisualStudio\devenv.exe"),
            launcher);

        var result = await ExecuteAsync(command, _root);

        Assert.True(result.Success);
        Assert.Equal(0, selector.CallCount);
        Assert.Equal((@"C:\VisualStudio\devenv.exe", solution), launcher.Opened);
    }

    [Fact]
    public async Task ExecuteAsync_AsksWhichSolutionToOpenWhenMultipleExist()
    {
        var first = Path.Combine(_root, "First.sln");
        var second = Path.Combine(_root, "Second.slnx");
        File.WriteAllText(first, "");
        File.WriteAllText(second, "");
        var selector = new RecordingItemSelection { ValueToReturn = second };
        var launcher = new RecordingSolutionLauncher();
        var command = new OpenSolutionCommand(
            selector,
            new StubVisualStudioLocator(@"C:\VisualStudio\devenv.exe"),
            launcher);

        await ExecuteAsync(command, _root);

        Assert.Equal(1, selector.CallCount);
        Assert.Equal(["First.sln", "Second.slnx"], selector.Items.Select(item => item.Label));
        Assert.Equal((@"C:\VisualStudio\devenv.exe", second), launcher.Opened);
    }

    [Fact]
    public async Task ExecuteAsync_ExplainsWhenNoSolutionExists()
    {
        var command = new OpenSolutionCommand(
            new RecordingItemSelection(),
            new StubVisualStudioLocator(@"C:\VisualStudio\devenv.exe"),
            new RecordingSolutionLauncher());

        var exception = await Assert.ThrowsAsync<CommandInputException>(
            () => ExecuteAsync(command, _root));

        Assert.Contains("未找到", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotLaunchWhenVisualStudioCannotBeLocated()
    {
        File.WriteAllText(Path.Combine(_root, "Only.sln"), "");
        var launcher = new RecordingSolutionLauncher();
        var command = new OpenSolutionCommand(
            new RecordingItemSelection(),
            new StubVisualStudioLocator(null),
            launcher);

        var exception = await Assert.ThrowsAsync<CommandInputException>(
            () => ExecuteAsync(command, _root));

        Assert.Contains("Visual Studio", exception.Message);
        Assert.Null(launcher.Opened);
    }

    [Fact]
    public void FindSolutions_RejectsMissingPath()
    {
        var missingPath = Path.Combine(_root, "missing");

        var exception = Assert.Throws<CommandInputException>(
            () => OpenSolutionCommand.FindSolutions(missingPath));

        Assert.Contains(missingPath, exception.Message);
    }

    private bool IsTestSolution(string path) =>
        path.StartsWith(_root, StringComparison.OrdinalIgnoreCase);

    private static Task<CommandResult> ExecuteAsync(OpenSolutionCommand command, string path)
    {
        return command.ExecuteAsync(
            new Dictionary<string, object?> { ["filepath"] = path },
            CancellationToken.None);
    }

    private sealed class StubVisualStudioLocator(string? path) : IVisualStudioLocator
    {
        public string? Find() => path;
    }

    private sealed class RecordingSolutionLauncher : ISolutionLauncher
    {
        public (string VisualStudioPath, string SolutionPath)? Opened { get; private set; }

        public void Open(string visualStudioPath, string solutionPath)
        {
            Opened = (visualStudioPath, solutionPath);
        }
    }

    private sealed class RecordingItemSelection : IItemSelectionService
    {
        public int CallCount { get; private set; }
        public object? ValueToReturn { get; init; }
        public IReadOnlyList<SelectionItem<object>> Items { get; private set; } = [];

        public Task<T> SelectAsync<T>(
            string title,
            IReadOnlyList<SelectionItem<T>> items,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Items = items
                .Select(item => new SelectionItem<object>(
                    item.Value!,
                    item.Label,
                    item.Description))
                .ToList();
            return Task.FromResult((T)ValueToReturn!);
        }
    }
}
