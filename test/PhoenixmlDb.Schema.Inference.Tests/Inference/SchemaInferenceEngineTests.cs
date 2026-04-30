using PhoenixmlDb.Schema.Inference.Inference;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.Inference;

public class SchemaInferenceEngineTests
{
    [Fact]
    public void WidensTypeOnConflictingValues()
    {
        // First observe integer, then decimal — should widen to decimal
        var type = SchemaInferenceEngine.RefineType(XsdType.Integer, "3.14");
        Assert.Equal(XsdType.Decimal, type);

        // Integer then string — should widen to string
        type = SchemaInferenceEngine.RefineType(XsdType.Integer, "hello");
        Assert.Equal(XsdType.String, type);
    }

    [Fact]
    public void MultiFileInferenceBuildRicherSchema()
    {
        // Two different XML structures — inference should combine them.
        // (Original test passed JSON via SchemaGenerator.JsonToXml; here we
        // inline the equivalent XML directly so this test stays engine-only.)
        var xml1 = "<doc><order><id>A</id><total>100</total></order></doc>";
        var xml2 = "<doc><order><id>B</id><total>200</total><priority>high</priority></order></doc>";

        var engine = new SchemaInferenceEngine();
        engine.Observe(xml1);
        engine.Observe(xml2);

        var summary = engine.GetSummary();
        Assert.Equal(2, summary.DocumentsObserved);

        // "priority" only appears in one doc — should not be required
        var priority = summary.Elements.FirstOrDefault(e => e.Name == "priority");
        Assert.NotNull(priority);
        Assert.False(priority!.IsRequired);

        // "id" appears in both — should be required (relative to parent)
        var id = summary.Elements.FirstOrDefault(e => e.Name == "id");
        Assert.NotNull(id);
    }
}
