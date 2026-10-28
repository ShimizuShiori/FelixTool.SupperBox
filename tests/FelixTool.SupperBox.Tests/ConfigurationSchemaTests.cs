using System.IO;
using System.Text.Json;

namespace FelixTool.SupperBox.Tests;

public sealed class ConfigurationSchemaTests : IDisposable
{
    private readonly string _applicationData = Path.Combine(
        Path.GetTempPath(), $"SupperBoxSchema-{Guid.NewGuid():N}");

    private string ConfigurationDirectory => Path.Combine(_applicationData, "FelixTool.SupperBox");
    private string ConfigurationPath => Path.Combine(ConfigurationDirectory, "config.json");
    private string SchemaPath => Path.Combine(ConfigurationDirectory, "config.schema.json");
    private static string BundledSchemaPath => Path.Combine(AppContext.BaseDirectory, "config.schema.json");

    public ConfigurationSchemaTests() => Directory.CreateDirectory(ConfigurationDirectory);

    public void Dispose() => Directory.Delete(_applicationData, true);

    [Fact]
    public void NewConfiguration_CopiesBundledSchemaAndAddsRelativeReference()
    {
        using var configuration = new SystemConfiguration(_applicationData);
        using var document = JsonDocument.Parse(File.ReadAllText(ConfigurationPath));

        Assert.Equal(File.ReadAllText(BundledSchemaPath), File.ReadAllText(SchemaPath));
        Assert.Equal(@".\config.schema.json", document.RootElement.GetProperty("$schema").GetString());
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        Assert.Equal(@".\config.schema.json",
            schema.RootElement.GetProperty("properties").GetProperty("$schema").GetProperty("default").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("old.schema.json")]
    [InlineData("config.schema.json")]
    public void ExistingConfiguration_AddsSchemaReferenceWithoutLosingUserSettings(string? reference)
    {
        File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["$schema"] = reference,
            ["repositories"] = new { roots = new[] { @"C:\repos", @"D:\repos" } },
            ["customPlugin"] = new { value = "custom", count = 3 }
        }));

        using var configuration = new SystemConfiguration(_applicationData);
        using var document = JsonDocument.Parse(File.ReadAllText(ConfigurationPath));
        var root = document.RootElement;

        Assert.Equal(@".\config.schema.json", root.GetProperty("$schema").GetString());
        Assert.Equal([@"C:\repos", @"D:\repos"],
            root.GetProperty("repositories").GetProperty("roots")
                .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("custom", root.GetProperty("customPlugin").GetProperty("value").GetString());
        Assert.Equal(3, root.GetProperty("customPlugin").GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"x-schema-version":0}""")]
    [InlineData("""{"x-schema-version":1}""")]
    [InlineData("""{"x-schema-version":2}""")]
    [InlineData("""{"x-schema-version":3}""")]
    public void ExistingSchema_ReplacesUnversionedOrOlderSchema(string schema)
    {
        File.WriteAllText(SchemaPath, schema);

        using var configuration = new SystemConfiguration(_applicationData);

        Assert.Equal(File.ReadAllText(BundledSchemaPath), File.ReadAllText(SchemaPath));
    }

    [Fact]
    public void ExistingConfiguration_AddsSchemaReferenceWithReadableTextAndUnchangedValues()
    {
        const string value = "汉字 & <tag> + ' \" \\ \n \t";
        File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
        {
            quicktexts = new Dictionary<string, string> { ["中文"] = value }
        }));

        using var configuration = new SystemConfiguration(_applicationData);
        var text = File.ReadAllText(ConfigurationPath);
        using var document = JsonDocument.Parse(text);

        Assert.Contains("中文", text);
        Assert.Contains("汉字 & <tag> + '", text);
        Assert.DoesNotContain("\\u", text);
        Assert.Equal(value, document.RootElement.GetProperty("quicktexts").GetProperty("中文").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExistingSchema_PreservesSameOrNewerVersionAndUnchangedConfiguration(int versionIncrement)
    {
        using var bundled = JsonDocument.Parse(File.ReadAllText(BundledSchemaPath));
        var version = bundled.RootElement.GetProperty("x-schema-version").GetInt32();
        var schema = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["x-schema-version"] = version + versionIncrement,
            ["title"] = "Do not overwrite this schema."
        });
        const string json = """{"$schema":".\\config.schema.json","customPlugin":{"value":"unchanged"}}""";
        File.WriteAllText(SchemaPath, schema);
        File.WriteAllText(ConfigurationPath, json);

        using var configuration = new SystemConfiguration(_applicationData);

        Assert.Equal(schema, File.ReadAllText(SchemaPath));
        Assert.Equal(json, File.ReadAllText(ConfigurationPath));
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"x-schema-version":"old"}""")]
    [InlineData("""{"x-schema-version":-1}""")]
    public void ExistingSchema_ReportsInvalidMetadata(string schema)
    {
        File.WriteAllText(SchemaPath, schema);

        Assert.Throws<InvalidDataException>(() => new SystemConfiguration(_applicationData));
        Assert.Equal(schema, File.ReadAllText(SchemaPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationReload_RestoresMissingOrOutdatedSchema(bool outdated)
    {
        using var configuration = new SystemConfiguration(_applicationData);
        if (outdated)
        {
            File.WriteAllText(SchemaPath, """{"x-schema-version":0}""");
        }
        else
        {
            File.Delete(SchemaPath);
        }

        var reloaded = new TaskCompletionSource<ConfigurationReloadedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        configuration.Reloaded += (_, args) => reloaded.TrySetResult(args);
        File.WriteAllText(ConfigurationPath, """{"$schema":".\\config.schema.json","customPlugin":{}}""");

        var result = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Equal(File.ReadAllText(BundledSchemaPath), File.ReadAllText(SchemaPath));
    }
}
