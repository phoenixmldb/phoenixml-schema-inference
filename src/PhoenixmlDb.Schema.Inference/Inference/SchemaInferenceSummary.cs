namespace PhoenixmlDb.Schema.Inference.Inference;

/// <summary>
/// Summary of what the schema inference engine observed across all documents.
/// </summary>
public sealed class SchemaInferenceSummary
{
    public int DocumentsObserved { get; init; }
    public List<InferredElementSummary> Elements { get; init; } = [];
}

/// <summary>
/// Summary of a single observed element, including the inferred type, occurrence
/// statistics, and structural metadata used by the inference engine.
/// </summary>
public sealed class InferredElementSummary
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string InferredType { get; init; }
    public bool IsRequired { get; init; }
    public double OccurrenceRate { get; init; }
    public bool IsArray { get; init; }
    public bool HasAttributes { get; init; }
    public List<string> SampleValues { get; init; } = [];
}
