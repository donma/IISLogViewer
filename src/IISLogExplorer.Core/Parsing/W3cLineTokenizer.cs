namespace IISLogExplorer.Core.Parsing;

public static class W3cLineTokenizer
{
    public static IReadOnlyList<ReadOnlyMemory<char>> Tokenize(string line)
    {
        var tokens = new List<ReadOnlyMemory<char>>();
        var tokenStart = -1;
        var tokenEnd = -1;
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (tokenStart >= 0)
                {
                    tokenEnd = index;
                }
                else
                {
                    tokenStart = index + 1;
                }

                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (tokenStart >= 0)
                {
                    tokens.Add(line.AsMemory(tokenStart, (tokenEnd > tokenStart ? tokenEnd : index) - tokenStart));
                    tokenStart = -1;
                    tokenEnd = -1;
                }

                continue;
            }

            if (tokenStart < 0)
            {
                tokenStart = index;
            }
        }

        if (tokenStart >= 0)
        {
            tokens.Add(line.AsMemory(tokenStart, (tokenEnd > tokenStart ? tokenEnd : line.Length) - tokenStart));
        }

        return tokens;
    }
}