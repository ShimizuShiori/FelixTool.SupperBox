using System.Text;

namespace FelixTool.SupperBox;

public static class CommandParser
{
    public static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var tokenStarted = false;

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character == '\\'
                && index + 1 < input.Length
                && input[index + 1] is '\\' or '"' or '\'')
            {
                current.Append(input[++index]);
                tokenStarted = true;
                continue;
            }

            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(character);
                }

                tokenStarted = true;
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                tokenStarted = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                AddToken(tokens, current, ref tokenStarted);
            }
            else
            {
                current.Append(character);
                tokenStarted = true;
            }
        }

        if (quote is not null)
        {
            throw new CommandInputException("引号没有闭合。");
        }

        AddToken(tokens, current, ref tokenStarted);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder current, ref bool tokenStarted)
    {
        if (!tokenStarted)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
        tokenStarted = false;
    }
}

public sealed class CommandInputException(string message) : Exception(message);
