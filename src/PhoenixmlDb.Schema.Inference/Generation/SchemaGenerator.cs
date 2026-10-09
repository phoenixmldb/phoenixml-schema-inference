using PhoenixmlDb.Schema.Inference.Conversion;
using PhoenixmlDb.Schema.Inference.Inference;

namespace PhoenixmlDb.Schema.Inference.Generation;

/// <summary>
/// High-level schema generator. Accepts XML, JSON, CSV, or a file path
/// (auto-detected by extension), converts the input to XML when needed, and
/// runs <see cref="SchemaInferenceEngine"/> to produce an XSD.
///
/// EDI input is intentionally NOT supported here. EDI disassembly is
/// domain-specific and depends on transport pipeline infrastructure; consume
/// this library from a host that brings its own EDI parser if needed.
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
                "Convert EDI to XML upstream and pass via FromXml."),
            _ => FromXml(content)
        };
    }

    /// <summary>
    /// Generates a schema from JSON input. The schema is inferred from the JSON itself, so an
    /// array is an array when it has one item, a number is a number, and null is nillable.
    /// </summary>
    /// <param name="json">The JSON document.</param>
    /// <param name="rootElement">The root element of the element-per-key XML.</param>
    /// <param name="form">
    /// Which XML the schema describes. <see cref="JsonSchemaForm.ElementPerKey"/>, the default,
    /// types every key. <see cref="JsonSchemaForm.JsonXml"/> describes the XML form of JSON
    /// (<c>fn:json-to-xml</c>), where XSD 1.0 can check kinds and keys and not the type of a key.
    /// </param>
    public static SchemaGenerationResult FromJson(string json, string rootElement = "root",
        JsonSchemaForm form = JsonSchemaForm.ElementPerKey)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(rootElement);

        var engine = new JsonSchemaInferenceEngine();
        engine.Observe(json);
        if (form == JsonSchemaForm.JsonXml)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return new SchemaGenerationResult
            {
                Xsd = engine.GenerateJsonXmlXsd(),
                Xml = Internal.JsonXml.FromJson(document.RootElement).ToString(),
                Summary = engine.GetElementSummary(rootElement),
                JsonSummary = engine.GetSummary(),
                Source = "Generated from JSON, for the XML form of JSON.",
            };
        }
        return new SchemaGenerationResult
        {
            Xsd = engine.GenerateXsd(rootElement),
            Xml = JsonToXml.Convert(json, rootElement),
            Summary = engine.GetElementSummary(rootElement),
            JsonSummary = engine.GetSummary(),
            Source = $"Generated from JSON. Root element: {rootElement}",
        };
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

/// <summary>Which XML rendering of JSON a schema describes.</summary>
public enum JsonSchemaForm
{
    /// <summary>
    /// One element for each key, as <see cref="JsonToXml"/> writes it. Every key has its own type.
    /// </summary>
    ElementPerKey,

    /// <summary>
    /// The XML form of JSON that <c>fn:json-to-xml</c> writes: <c>map</c>, <c>array</c>,
    /// <c>string</c>, <c>number</c>, <c>boolean</c>, <c>null</c>, with the key in an attribute.
    /// </summary>
    JsonXml,
}
