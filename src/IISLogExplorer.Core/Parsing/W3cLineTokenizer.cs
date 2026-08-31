namespace IISLogExplorer.Core.Parsing;

public static class W3cLineTokenizer
{
    public static IReadOnlyList<string> Tokenize(string line)
    {
        var values = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (started)
                {
                    values.Add(value.ToString());
                    value.Clear();
                    started = false;
                }
            }
            else
            {
                value.Append(character);
                started = true;
            }
        }

        if (started)
        {
            values.Add(value.ToString());
        }

        return values;
    }
}
