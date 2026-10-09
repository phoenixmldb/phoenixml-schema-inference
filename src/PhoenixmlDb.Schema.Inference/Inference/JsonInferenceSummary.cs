namespace PhoenixmlDb.Schema.Inference.Inference;

/// <summary>The kinds of JSON value observed at one place.</summary>
[Flags]
public enum JsonKinds
{
    None = 0,
    Object = 1,
    Array = 2,
    String = 4,
    Number = 8,
    Boolean = 16,
    Null = 32,
}

/// <summary>What a <see cref="JsonSchemaInferenceEngine"/> observed, by JSON path.</summary>
public sealed class JsonInferenceSummary
{
    public int DocumentsObserved { get; init; }

    /// <summary>One entry for each place a value was observed, the root (<c>$</c>) first.</summary>
    public IReadOnlyList<JsonMemberSummary> Members { get; init; } = [];
}

/// <summary>What was observed at one JSON path.</summary>
public sealed class JsonMemberSummary
{
    /// <summary>The path: <c>$</c>, <c>$.order.lines[*].sku</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Every kind of value observed here.</summary>
    public JsonKinds Kinds { get; init; }

    /// <summary>The XSD type of the plain values here; null when there were none.</summary>
    public string? ValueType { get; init; }

    /// <summary>
    /// True when every observed object that could have this member had it. For the items of an
    /// array: true when no observed array was empty.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>True when null was observed here.</summary>
    public bool IsNullable { get; init; }

    /// <summary>How many values were observed here.</summary>
    public int Occurrences { get; init; }

    /// <summary>The length of the shortest array observed here; null when there was none.</summary>
    public int? MinItems { get; init; }

    /// <summary>The length of the longest array observed here; null when there was none.</summary>
    public int? MaxItems { get; init; }

    public IReadOnlyList<string> SampleValues { get; init; } = [];
}
