using System.Text.Json;
using PhoenixmlDb.Schema.Inference.Conversion;
using PhoenixmlDb.Schema.Inference.Generation;
using PhoenixmlDb.Schema.Inference.Inference;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.RoundTrip;

public class JsonRoundTripTests
{
    [Fact]
    public void JsonWithTypedValuesRoundTripsViaInferredSchema()
    {
        var originalJson = """
            {
              "order": {
                "id": "ORD-1",
                "total": 1500.00,
                "active": true
              }
            }
            """;

        // JSON → XML; engine infers XSD
        var generated = SchemaGenerator.FromJson(originalJson, "doc");

        // XML + inferred XSD → typed JSON
        var roundTrippedJson = XmlToJson.Convert(generated.Xml, generated.Xsd);

        using var originalDoc = JsonDocument.Parse(originalJson);
        using var roundTrippedDoc = JsonDocument.Parse(roundTrippedJson);

        // The round-trip wraps everything in the configured root name ("doc").
        var originalOrder = originalDoc.RootElement.GetProperty("order");
        var roundTrippedOrder = roundTrippedDoc.RootElement
            .GetProperty("doc")
            .GetProperty("order");

        // String survives as string.
        Assert.Equal(
            originalOrder.GetProperty("id").GetString(),
            roundTrippedOrder.GetProperty("id").GetString());

        // Decimal survives as JSON number (not string), with value preserved.
        Assert.Equal(JsonValueKind.Number, roundTrippedOrder.GetProperty("total").ValueKind);
        Assert.Equal(
            originalOrder.GetProperty("total").GetDecimal(),
            roundTrippedOrder.GetProperty("total").GetDecimal());

        // Boolean survives as JSON boolean (not string).
        Assert.Equal(JsonValueKind.True, roundTrippedOrder.GetProperty("active").ValueKind);
        Assert.True(roundTrippedOrder.GetProperty("active").GetBoolean());
    }

    [Fact]
    public void JsonArraysRoundTripAsJsonArraysViaInferredSchema()
    {
        var originalJson = """
            {
              "items": [
                { "name": "A", "qty": 1 },
                { "name": "B", "qty": 2 }
              ]
            }
            """;

        // JSON → XML produces repeated <items> sibling elements.
        // The schema engine notices repetition and emits maxOccurs="unbounded".
        // XmlToJson reads that XSD and emits a JSON array.
        var generated = SchemaGenerator.FromJson(originalJson, "doc");
        var roundTrippedJson = XmlToJson.Convert(generated.Xml, generated.Xsd);

        using var roundTrippedDoc = JsonDocument.Parse(roundTrippedJson);
        var items = roundTrippedDoc.RootElement
            .GetProperty("doc")
            .GetProperty("items");

        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(2, items.GetArrayLength());

        Assert.Equal("A", items[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Number, items[0].GetProperty("qty").ValueKind);
        Assert.Equal(1L, items[0].GetProperty("qty").GetInt64());

        Assert.Equal("B", items[1].GetProperty("name").GetString());
        Assert.Equal(2L, items[1].GetProperty("qty").GetInt64());
    }

    [Fact]
    public void SingleOccurrenceUnderArraySchemaStillEmitsAsArray()
    {
        // Multi-doc inference: 2 docs, one with 1 item, one with 2 items.
        // Schema sees "items" repeating in doc 2 → maxOccurs="unbounded".
        // Round-tripping doc 1 (which has only 1 item) must still emit a JSON array,
        // not a scalar — schema-driven array detection in XmlToJson.
        var json1 = """{ "items": [ { "name": "Solo" } ] }""";
        var json2 = """{ "items": [ { "name": "X" }, { "name": "Y" } ] }""";

        // Use the engine directly to fold both observations into one schema.
        var engine = new SchemaInferenceEngine();
        var xml1 = JsonToXml.Convert(json1, "doc");
        var xml2 = JsonToXml.Convert(json2, "doc");
        engine.Observe(xml1);
        engine.Observe(xml2);
        var xsd = engine.GenerateXsd();

        var roundTripped = XmlToJson.Convert(xml1, xsd);

        using var doc = JsonDocument.Parse(roundTripped);
        var items = doc.RootElement.GetProperty("doc").GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Solo", items[0].GetProperty("name").GetString());
    }
}
