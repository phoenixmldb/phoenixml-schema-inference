using PhoenixmlDb.Schema.Inference.Generation;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.Generation;

public class SchemaGeneratorTests
{
    [Fact]
    public void FromJsonProducesXmlAndXsdAndSummary()
    {
        var json = """{ "order": { "id": "X", "total": 1.5 } }""";

        var result = SchemaGenerator.FromJson(json, "doc");

        Assert.NotEmpty(result.Xsd);
        Assert.NotEmpty(result.Xml);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.DocumentsObserved >= 1);
    }

    [Fact]
    public void FromXmlObservesAndReturnsXsd()
    {
        var xml = "<order><id>X</id><total>1.5</total></order>";

        var result = SchemaGenerator.FromXml(xml);

        Assert.Contains("name=\"order\"", result.Xsd);
        Assert.Contains("name=\"id\"", result.Xsd);
    }

    [Fact]
    public void FromFileThrowsOnEdiExtension()
    {
        var tmp = Path.GetTempFileName();
        var ediPath = tmp + ".edi";
        File.Move(tmp, ediPath);
        File.WriteAllText(ediPath, "ISA*00*...");
        try
        {
            Assert.Throws<NotSupportedException>(() => SchemaGenerator.FromFile(ediPath));
        }
        finally
        {
            File.Delete(ediPath);
        }
    }
}
