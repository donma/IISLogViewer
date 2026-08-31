namespace IISLogExplorer.Core.Parsing;

public sealed record FieldDefinition(string Name, int Index);

public sealed class FieldsHeaderParser
{
    public IReadOnlyList<FieldDefinition> Parse(string line)
    {
        if (!line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var fields = line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Select((name, index) => new FieldDefinition(name, index)).ToArray();
    }
}
