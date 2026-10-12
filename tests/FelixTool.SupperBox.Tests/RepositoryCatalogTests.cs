using System.IO;
using System.Text.Json;

namespace FelixTool.SupperBox.Tests;

public sealed class RepositoryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"SupperBoxCatalog-{Guid.NewGuid():N}");

    public RepositoryCatalogTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task ApplyConfiguration_ReplacesRootsAndSupportsMultipleDirectories()
    {
        var firstRoot = Path.Combine(_root, "First");
        var secondRoot = Path.Combine(_root, "Second");
        var firstRepository = Directory.CreateDirectory(Path.Combine(firstRoot, "Alpha")).FullName;
        var secondRepository = Directory.CreateDirectory(Path.Combine(secondRoot, "Beta")).FullName;
        var catalog = new RepositoryCatalog(_root);
        var selection = new RecordingRepositorySelection();

        ApplyRoots(catalog, [firstRoot, secondRoot, firstRoot + Path.DirectorySeparatorChar,
            Path.Combine(_root, "Missing")]);

        Assert.Equal([firstRepository, secondRepository], catalog.GetAll());
        Assert.Equal(secondRepository, await catalog.SelectAsync("beta", selection, CancellationToken.None));
        Assert.Null(await catalog.SelectAsync("Missing", selection, CancellationToken.None));
        Assert.Equal(0, selection.CallCount);

        ApplyRoots(catalog, [secondRoot]);

        Assert.Equal([secondRepository], catalog.GetAll());
        Assert.Null(await catalog.SelectAsync("Alpha", selection, CancellationToken.None));

        ApplyRoots(catalog, []);

        Assert.Empty(catalog.GetAll());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""{"roots":"C:\\repos"}""")]
    [InlineData("""{"roots":[null]}""")]
    [InlineData("""{"roots":[123]}""")]
    [InlineData("""{"roots":[""]}""")]
    [InlineData("""{"roots":[" "]}""")]
    [InlineData("""{"roots":["relative"]}""")]
    public void ApplyConfiguration_RejectsInvalidRootsAndPreservesPreviousConfiguration(string json)
    {
        var repository = Directory.CreateDirectory(Path.Combine(_root, "Alpha")).FullName;
        var catalog = new RepositoryCatalog(_root);
        using var document = JsonDocument.Parse(json);

        Assert.Throws<InvalidDataException>(() => catalog.ApplyConfiguration(document.RootElement));
        Assert.Equal([repository], catalog.GetAll());
    }

    [Fact]
    public async Task SelectAsync_RespectsCancellationBeforeSingleMatch()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        var catalog = new RepositoryCatalog(_root);

        await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.SelectAsync(
            "Alpha", new RecordingRepositorySelection(), new CancellationToken(true)));
    }

    private static void ApplyRoots(RepositoryCatalog catalog, string[] roots)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { roots }));
        catalog.ApplyConfiguration(document.RootElement);
    }
}
