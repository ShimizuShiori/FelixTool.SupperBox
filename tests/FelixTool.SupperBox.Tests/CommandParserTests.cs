namespace FelixTool.SupperBox.Tests;

public sealed class CommandParserTests
{
    [Fact]
    public void Tokenize_SupportsQuotedAndEmptyArguments()
    {
        var tokens = CommandParser.Tokenize("echo \"hello world\" ''");

        Assert.Equal(["echo", "hello world", string.Empty], tokens);
    }

    [Fact]
    public void Tokenize_PreservesWindowsPaths()
    {
        var tokens = CommandParser.Tokenize(@"open C:\Temp\file.txt");

        Assert.Equal(@"C:\Temp\file.txt", tokens[1]);
    }

    [Fact]
    public void Tokenize_RejectsUnclosedQuote()
    {
        var exception = Assert.Throws<CommandInputException>(
            () => CommandParser.Tokenize("echo \"unfinished"));

        Assert.Contains("引号", exception.Message);
    }
}
