using PhoenixmlDb.Schema.Inference.Conversion;
using PhoenixmlDb.Schema.Inference.Inference;

namespace PhoenixmlDb.Schema.Inference.Generation;

/// <summary>
/// High-level schema generator. Accepts XML, JSON, CSV, or a file path
/// (auto-detected by extension), converts the input to XML when needed, and
/// runs <see cref="SchemaInferenceEngine"/> to produce an XSD.
///
/// EDI input is intentionally NOT supported in this open-source package — it
/// remains in ConduitXP because the disassembly logic is domain-specific.
/// </summary>
public static class SchemaGenerator
{
    /// <summary>
    /// Auto-detects format from file extension and generates a schema.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when the file extension is .edi or .x12.</exception>
    public static SchemaGenerationResult FromFile(string filePath, string? rootElement = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var content = File.ReadAllText(filePath);
        var ext = Path.GetExtension(filePath).ToUpperInvariant();
        return ext switch
        {
            ".JSON" => FromJson(content, rootElement ?? Path.GetFileNameWithoutExtension(filePath)),
            ".CSV" => FromCsv(content, rootElement ?? "rows", "row", ","),
            ".TSV" => FromCsv(content, rootElement ?? "rows", "row", "\t"),
            ".XML" or ".XSL" or ".XSLT" => FromXml(content),
            ".EDI" or ".X12" => throw new NotSupportedException(
                "EDI input is not supported in PhoenixmlDb.Schema.Inference. " +
                "Use ConduitXP for EDI disassembly."),
            _ => FromXml(content)
        };
    }

    /// <summary>Generates a schema from JSON input.</summary>
    public static SchemaGenerationResult FromJson(string json, string rootElement = "root")
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(rootElement);

        var xml = JsonToXml.Convert(json, rootElement);
        return FromXmlInternal(xml, $"Generated from JSON. Root element: {rootElement}");
    }

    /// <summary>Generates a schema from CSV input.</summary>
    public static SchemaGenerationResult FromCsv(
        string csv, string rootElement = "rows",
        string rowElement = "row", string delimiter = ",")
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(rootElement);
        ArgumentNullException.ThrowIfNull(rowElement);
        ArgumentNullException.ThrowIfNull(delimiter);

        var xml = CsvToXml.Convert(csv, rootElement, rowElement, delimiter);
        return FromXmlInternal(xml, $"Generated from CSV. Root: {rootElement}, Row: {rowElement}");
    }

    /// <summary>Generates a schema from XML input.</summary>
    public static SchemaGenerationResult FromXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        return FromXmlInternal(xml, "Generated from XML document.");
    }

    private static SchemaGenerationResult FromXmlInternal(string xml, string source)
    {
        var engine = new SchemaInferenceEngine();
        engine.Observe(xml);
        return new SchemaGenerationResult
        {
            Xsd = engine.GenerateXsd(),
            Xml = xml,
            Summary = engine.GetSummary(),
            Source = source
        };
    }
}
