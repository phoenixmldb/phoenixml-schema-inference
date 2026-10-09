using System.Xml;

namespace PhoenixmlDb.Schema.Inference.Inference;

/// <summary>The lexical shape numbers had, narrowest first.</summary>
internal enum NumberShape
{
    None,
    Integer,
    Decimal,
    Double,
}

/// <summary>What strings were, narrowest first: every one a date, every one a date and time, or text.</summary>
internal enum StringShape
{
    None,
    Date,
    DateTime,
    Text,
}

internal static class StringShapes
{
    public static StringShape Refine(this StringShape current, string value)
    {
        var observed = Of(value);
        return current == StringShape.None || current == observed ? observed : StringShape.Text;
    }

    public static StringShape Merge(StringShape a, StringShape b) =>
        a == StringShape.None ? b : b == StringShape.None || a == b ? a : StringShape.Text;

    /// <summary>
    /// A string is a date only in the lexical form of xs:date or xs:dateTime: what a schema
    /// validator will accept, and the same on every machine.
    /// </summary>
    private static StringShape Of(string value)
    {
        if (value.Length is >= 10 and <= 16 && value[4] == '-' && value[7] == '-' && !value.Contains('T', StringComparison.Ordinal))
        {
            try { XmlConvert.ToDateTimeOffset(value, ["yyyy-MM-dd", "yyyy-MM-ddzzz", "yyyy-MM-ddZ"]); return StringShape.Date; }
            catch (FormatException) { return StringShape.Text; }
        }
        if (value.Length >= 19 && value[4] == '-' && value[10] == 'T')
        {
            try { XmlConvert.ToDateTimeOffset(value); return StringShape.DateTime; }
            catch (FormatException) { return StringShape.Text; }
        }
        return StringShape.Text;
    }
}

/// <summary>Everything observed at one place in the JSON documents.</summary>
internal sealed class JsonNode
{
    public JsonKinds Kinds { get; set; }
    public int Occurrences { get; set; }

    /// <summary>How many of the parent's objects had this member.</summary>
    public int PresentIn { get; set; }

    public int Objects { get; set; }
    public Dictionary<string, JsonNode> Members { get; } = new(StringComparer.Ordinal);
    public List<string> MemberOrder { get; } = [];

    public int Arrays { get; set; }
    public int MinItems { get; set; }
    public int MaxItems { get; set; }
    public JsonNode? Item { get; set; }

    public NumberShape Numbers { get; set; }
    public StringShape Strings { get; set; }
    public List<string> Samples { get; } = [];

    public void AddSample(string value)
    {
        if (Samples.Count < 5 && !Samples.Contains(value))
            Samples.Add(value);
    }

    /// <summary>
    /// The XSD type of the plain values here, or null when there were none. Values of
    /// different kinds have only xs:string in common.
    /// </summary>
    public string? ScalarType() => (Kinds & (JsonKinds.String | JsonKinds.Number | JsonKinds.Boolean)) switch
    {
        JsonKinds.None => null,
        JsonKinds.Boolean => "xs:boolean",
        JsonKinds.Number => Numbers switch
        {
            NumberShape.Integer => "xs:integer",
            NumberShape.Decimal => "xs:decimal",
            _ => "xs:double",
        },
        JsonKinds.String => Strings switch
        {
            StringShape.Date => "xs:date",
            StringShape.DateTime => "xs:dateTime",
            _ => "xs:string",
        },
        _ => "xs:string",
    };

    /// <summary>This node with what it was as an array left out.</summary>
    public JsonNode WithoutArray()
    {
        var copy = Merge([this]);
        copy.Kinds &= ~JsonKinds.Array;
        copy.Arrays = 0;
        copy.Item = null;
        return copy;
    }

    /// <summary>One node for everything observed at several places.</summary>
    public static JsonNode Merge(IEnumerable<JsonNode> nodes)
    {
        var merged = new JsonNode();
        foreach (var node in nodes)
        {
            merged.Kinds |= node.Kinds;
            merged.Occurrences += node.Occurrences;
            merged.PresentIn += node.PresentIn;
            merged.Numbers = (NumberShape)Math.Max((int)merged.Numbers, (int)node.Numbers);
            merged.Strings = StringShapes.Merge(merged.Strings, node.Strings);
            foreach (var sample in node.Samples)
                merged.AddSample(sample);

            if (node.Arrays > 0)
            {
                merged.MinItems = merged.Arrays == 0 ? node.MinItems : Math.Min(merged.MinItems, node.MinItems);
                merged.MaxItems = Math.Max(merged.MaxItems, node.MaxItems);
                merged.Arrays += node.Arrays;
            }
            if (node.Item is { } item)
                merged.Item = merged.Item is null ? Merge([item]) : Merge([merged.Item, item]);

            foreach (var key in node.MemberOrder)
            {
                var member = node.Members[key];
                if (merged.Members.TryGetValue(key, out var existing))
                {
                    merged.Members[key] = Merge([existing, member]);
                }
                else
                {
                    // A member another of the merged nodes never had is not in every object.
                    merged.Members[key] = Merge([member]);
                    merged.MemberOrder.Add(key);
                }
            }
            merged.Objects += node.Objects;
        }
        return merged;
    }
}
