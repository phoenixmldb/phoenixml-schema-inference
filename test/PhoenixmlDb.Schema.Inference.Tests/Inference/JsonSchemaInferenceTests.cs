using System.Text.Json;
using System.Xml.Linq;
using PhoenixmlDb.Core.Schema;
using PhoenixmlDb.Schema.Inference.Conversion;
using PhoenixmlDb.Schema.Inference.Generation;
using PhoenixmlDb.Schema.Inference.Inference;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.Inference;

/// <summary>
/// A schema inferred from JSON. Every schema here is compiled by the engine's schema compiler
/// (PhoenixmlDb.Core) and the documents are validated against it: a schema that only looks
/// right is not accepted.
/// </summary>
public class JsonSchemaInferenceTests
{
    private static readonly Uri SchemaUri = new("mem://inferred/schema.xsd");

    private static CompiledSchema Compile(string xsd) =>
        SchemaCompiler.Compile([SchemaUri], SchemaAccessGate.SuppliedOnly, [SchemaSource.FromText(xsd, SchemaUri)]);

    private static SchemaValidationResult Validate(string xsd, string xml) => SchemaValidator.Validate(Compile(xsd), xml);

    private static string Errors(SchemaValidationResult result) => string.Join(" | ", result.Diagnostics.Select(d => d.Message));

    /// <summary>The XML form of JSON as the XQuery engine's fn:json-to-xml writes it.</summary>
    private static async Task<string> JsonToXmlByTheEngine(string json)
    {
        var literal = json.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal);
        return await new XQueryFacade().EvaluateAsync($"json-to-xml(\"{literal}\")", cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static JsonSchemaInferenceEngine Observed(params string[] documents)
    {
        var engine = new JsonSchemaInferenceEngine();
        foreach (var document in documents)
            engine.Observe(document);
        return engine;
    }

    private const string Order = """
        {
          "id": "ORD-1",
          "total": 1500.50,
          "quantity": 10,
          "rate": 1.5e3,
          "placed": "2026-04-17",
          "shipped": "2026-04-18T09:30:00Z",
          "active": true,
          "note": null,
          "zip": "02139",
          "customer": { "name": "Ada", "tags": ["gold"] },
          "lines": [ { "sku": "A", "qty": 1 }, { "sku": "B", "qty": 2, "gift": true } ]
        }
        """;

    public static TheoryData<string> Documents => new()
    {
        Order,
        """{ "a": [] }""",
        """{ "a": [1, null, 3] }""",
        """{ "a": {} }""",
        """{ "a": [[1, 2], [3]] }""",
        """{ "first name": "x", "2nd": 2, "@type": "t" }""",
        """[ { "a": 1 }, { "a": 2, "b": "x" } ]""",
        """[ 1, 2, 3 ]""",
        """{ "a": [ { "b": [ { "c": null } ] } ] }""",
        """{ "text": "<&>", "empty": "" }""",
        "42",
        "\"text\"",
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheElementPerKeySchemaCompilesAndAcceptsTheDocumentItWasInferredFrom(string json)
    {
        var result = SchemaGenerator.FromJson(json, "doc");
        var validation = Validate(result.Xsd, result.Xml);
        Assert.True(validation.IsValid, Errors(validation) + "\n" + result.Xsd + "\n" + result.Xml);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task TheJsonXmlSchemaCompilesAndAcceptsWhatJsonToXmlWrites(string json)
    {
        var result = SchemaGenerator.FromJson(json, "doc", JsonSchemaForm.JsonXml);

        ArgumentNullException.ThrowIfNull(json);
        // Not as this library writes it: as the engine does.
        var written = await JsonToXmlByTheEngine(json);

        var validation = Validate(result.Xsd, written);
        Assert.True(validation.IsValid, Errors(validation) + "\n" + result.Xsd + "\n" + written);
        // And this library writes the same document.
        Assert.True(XNode.DeepEquals(Normalised(written), Normalised(result.Xml)), written + "\n" + result.Xml);
    }

    private static XElement Normalised(string xml)
    {
        var root = XElement.Parse(xml);
        foreach (var element in root.DescendantsAndSelf())
        {
            element.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
            // <string/> and <string></string> are the same document.
            if (!element.HasElements)
                element.Value = element.Value;
        }
        return root;
    }

    [Fact]
    public void EveryKeyHasItsOwnType()
    {
        var xsd = XElement.Parse(Observed(Order).GenerateXsd("doc"));
        string TypeOf(string name) => (string)xsd.Descendants().First(e => (string?)e.Attribute("name") == name).Attribute("type")!;

        Assert.Equal("xs:string", TypeOf("id"));
        Assert.Equal("xs:decimal", TypeOf("total"));
        Assert.Equal("xs:integer", TypeOf("quantity"));
        Assert.Equal("xs:double", TypeOf("rate"));
        Assert.Equal("xs:date", TypeOf("placed"));
        Assert.Equal("xs:dateTime", TypeOf("shipped"));
        Assert.Equal("xs:boolean", TypeOf("active"));
        // A string of digits is a string: JSON said so.
        Assert.Equal("xs:string", TypeOf("zip"));
    }

    [Fact]
    public void AnArrayOfOneItemIsStillAnArray()
    {
        var result = SchemaGenerator.FromJson(Order, "doc");
        Assert.Equal(JsonKinds.Array, result.JsonSummary!.Members.Single(m => m.Path == "$.customer.tags").Kinds);

        // …so it comes back as an array.
        using var back = JsonDocument.Parse(XmlToJson.Convert(result.Xml, result.Xsd));
        Assert.Equal(JsonValueKind.Array, back.RootElement.GetProperty("doc").GetProperty("customer").GetProperty("tags").ValueKind);
    }

    [Fact]
    public void NullIsNillableAndComesBackAsNull()
    {
        var result = SchemaGenerator.FromJson(Order, "doc");
        var note = XElement.Parse(result.Xsd).Descendants().First(e => (string?)e.Attribute("name") == "note");
        Assert.Equal("true", (string?)note.Attribute("nillable"));

        using var back = JsonDocument.Parse(XmlToJson.Convert(result.Xml, result.Xsd));
        Assert.Equal(JsonValueKind.Null, back.RootElement.GetProperty("doc").GetProperty("note").ValueKind);
    }

    [Fact]
    public void AKeyEveryObjectHasIsRequiredAndOneSomeLackIsNot()
    {
        var engine = Observed("""{ "id": 1, "name": "a" }""", """{ "id": 2 }""");
        var xsd = engine.GenerateXsd("doc");

        Assert.True(Validate(xsd, "<doc><id>3</id></doc>").IsValid);
        Assert.False(Validate(xsd, "<doc><name>a</name></doc>").IsValid);

        // Inside an array too: required is counted over the objects, not over the documents.
        var lines = Observed(Order).GenerateXsd("doc");
        var line = XElement.Parse(lines).Descendants().First(e => (string?)e.Attribute("name") == "lines");
        Assert.Null(line.Descendants().First(e => (string?)e.Attribute("name") == "sku").Attribute("minOccurs"));
        Assert.Equal("0", (string?)line.Descendants().First(e => (string?)e.Attribute("name") == "gift").Attribute("minOccurs"));
    }

    [Fact]
    public void TheMembersOfAnObjectAreInAnyOrder()
    {
        var xsd = Observed("""{ "a": 1, "b": "x", "c": true }""").GenerateXsd("doc");
        Assert.True(Validate(xsd, "<doc><c>false</c><a>2</a><b>y</b></doc>").IsValid);
    }

    [Theory]
    [InlineData("<doc><a>not a number</a><b>x</b></doc>")]
    [InlineData("<doc><a>1</a><b>x</b><extra/></doc>")]
    [InlineData("<doc><a>1</a></doc>")]
    public void ADocumentThatDoesNotFitIsRefused(string xml)
    {
        var xsd = Observed("""{ "a": 1, "b": "x" }""").GenerateXsd("doc");
        Assert.False(Validate(xsd, xml).IsValid);
    }

    public static TheoryData<string[]> DocumentSets => new()
    {
        // A key that is a number, null, absent; an array in one document and a plain value in another.
        new[] { """{ "a": 1, "b": [1] }""", """{ "b": 2, "a": null }""", """{ "b": [] }""" },
        // An object in one document and a plain value in another.
        new[] { """{ "a": { "x": 1 } }""", """{ "a": "text" }""" },
        // Two keys that become the same element name.
        new[] { """{ "a b": 1, "a-b": "two" }""" },
        // Objects in an array that do not have the same members.
        new[] { """{ "rows": [ { "id": 1 }, { "id": 2, "tags": ["x", "y"] }, { "id": 3, "tags": [] } ] }""" },
        // Nothing but null, and nothing at all.
        new[] { """{ "a": null }""", """{}""" },
    };

    [Theory]
    [MemberData(nameof(DocumentSets))]
    public void OneSchemaAcceptsEveryDocumentItWasInferredFrom(string[] documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var xsd = Observed(documents).GenerateXsd("doc");
        var schema = Compile(xsd);
        foreach (var document in documents)
        {
            var xml = JsonToXml.Convert(document, "doc");
            var validation = SchemaValidator.Validate(schema, xml, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(validation.IsValid, Errors(validation) + "\n" + xsd + "\n" + xml);
        }
    }

    /// <summary>
    /// The limit of XSD 1.0 this pins: an object with a repeating member is a repeated choice.
    /// Its members are in any order and each is typed, but a required one that is missing is
    /// not noticed.
    /// </summary>
    [Fact]
    public void AnObjectWithARepeatingMemberIsInAnyOrderAndDoesNotCheckRequired()
    {
        var engine = Observed("""{ "x": [1, 2], "y": "a" }""");
        var xsd = engine.GenerateXsd("doc");

        Assert.True(Validate(xsd, "<doc><x>1</x><x>2</x><y>a</y></doc>").IsValid);
        Assert.True(Validate(xsd, "<doc><y>a</y><x>1</x></doc>").IsValid);
        Assert.False(Validate(xsd, "<doc><x>one</x><y>a</y></doc>").IsValid);
        // "y" is required, the summary says so, and the schema cannot.
        Assert.True(engine.GetSummary().Members.Single(m => m.Path == "$.y").IsRequired);
        Assert.True(Validate(xsd, "<doc><x>1</x></doc>").IsValid);
    }

    [Fact]
    public void TypesWidenAcrossDocuments()
    {
        var engine = Observed("""{ "n": 1, "d": "2026-01-01", "m": 1 }""", """{ "n": 1.5, "d": "soon", "m": "one" }""");
        var summary = engine.GetSummary();
        string? TypeAt(string path) => summary.Members.Single(m => m.Path == path).ValueType;

        Assert.Equal("xs:decimal", TypeAt("$.n"));
        Assert.Equal("xs:string", TypeAt("$.d"));
        Assert.Equal("xs:string", TypeAt("$.m"));
        Assert.Equal(JsonKinds.Number | JsonKinds.String, summary.Members.Single(m => m.Path == "$.m").Kinds);
    }

    [Fact]
    public void TheSummaryIsByJsonPath()
    {
        var summary = Observed(Order).GetSummary();
        var paths = summary.Members.Select(m => m.Path).ToList();

        Assert.Equal("$", paths[0]);
        Assert.Contains("$.customer.tags[*]", paths);
        Assert.Contains("$.lines[*].sku", paths);

        var gift = summary.Members.Single(m => m.Path == "$.lines[*].gift");
        Assert.False(gift.IsRequired);
        Assert.True(summary.Members.Single(m => m.Path == "$.lines[*].sku").IsRequired);
        Assert.True(summary.Members.Single(m => m.Path == "$.note").IsNullable);
        Assert.Equal(2, summary.Members.Single(m => m.Path == "$.lines").MaxItems);
    }

    [Fact]
    public async Task ADocumentInTheXmlFormIsObservedAsTheJsonItStandsFor()
    {
        var written = await JsonToXmlByTheEngine(Order);
        var fromXml = new JsonSchemaInferenceEngine();
        fromXml.ObserveJsonXml(written);

        Assert.Equal(Observed(Order).GenerateXsd("doc"), fromXml.GenerateXsd("doc"));
    }

    [Fact]
    public void TheJsonXmlSchemaChecksKindsAndKeys()
    {
        var xsd = Observed("""{ "a": 1, "b": ["x"] }""").GenerateJsonXmlXsd();
        const string Ns = "xmlns='http://www.w3.org/2005/xpath-functions'";

        Assert.True(Validate(xsd, $"<map {Ns}><number key='a'>2</number></map>").IsValid);
        // A key that was never observed.
        Assert.False(Validate(xsd, $"<map {Ns}><number key='zz'>2</number></map>").IsValid);
        // The same key twice.
        Assert.False(Validate(xsd, $"<map {Ns}><number key='a'>2</number><number key='a'>3</number></map>").IsValid);
        // A kind that was never observed inside a map.
        Assert.False(Validate(xsd, $"<map {Ns}><boolean key='a'>true</boolean></map>").IsValid);
        // Not a number.
        Assert.False(Validate(xsd, $"<map {Ns}><number key='a'>two</number></map>").IsValid);
        // What it cannot check, and says so: the type of a key. "b" was an array.
        Assert.True(Validate(xsd, $"<map {Ns}><number key='b'>2</number></map>").IsValid);
    }
}
