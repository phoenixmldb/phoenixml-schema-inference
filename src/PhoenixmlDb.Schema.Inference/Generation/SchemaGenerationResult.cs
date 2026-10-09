using PhoenixmlDb.Schema.Inference.Inference;

namespace PhoenixmlDb.Schema.Inference.Generation;

/// <summary>
/// Result of a schema generation: the inferred XSD, the (possibly converted)
/// XML used to infer it, an optional structural summary, and a source-format
/// description.
/// </summary>
public sealed class SchemaGenerationResult
{
    /// <summary>The generated XSD schema.</summary>
    public required string Xsd { get; init; }

    /// <summary>The XML representation of the input (useful for seeing how JSON/CSV mapped).</summary>
    public required string Xml { get; init; }

    /// <summary>Inference summary with element details.</summary>
    public SchemaInferenceSummary? Summary { get; init; }

    /// <summary>For JSON input: what was observed, by JSON path. Null for other inputs.</summary>
    public JsonInferenceSummary? JsonSummary { get; init; }

    /// <summary>Description of the source format.</summary>
    public required string Source { get; init; }
}
