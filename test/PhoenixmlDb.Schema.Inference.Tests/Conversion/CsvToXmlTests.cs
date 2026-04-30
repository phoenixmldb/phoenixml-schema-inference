using System.Text.RegularExpressions;
using PhoenixmlDb.Schema.Inference.Conversion;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.Conversion;

public class CsvToXmlTests
{
    [Fact]
    public void HeaderAndTwoRowsProduceTwoRowElements()
    {
        var csv = "id,name\n1,Alice\n2,Bob\n";

        var xml = CsvToXml.Convert(csv, rootElement: "people", rowElement: "person");

        Assert.Contains("<people>", xml);
        Assert.Contains("<person>", xml);
        Assert.Equal(2, Regex.Count(xml, "<person>"));
        Assert.Contains("<name>Alice</name>", xml);
        Assert.Contains("<name>Bob</name>", xml);
    }

    [Fact]
    public void TabDelimiterWorks()
    {
        var tsv = "a\tb\n1\t2\n";

        var xml = CsvToXml.Convert(tsv, "rows", "row", delimiter: "\t");

        Assert.Contains("<a>1</a>", xml);
        Assert.Contains("<b>2</b>", xml);
    }

    [Fact]
    public void HeaderNamesAreSanitizedForXml()
    {
        // Source sanitizer: spaces become dashes (NOT underscores)
        var csv = "first name,last-name\nAlice,Smith\n";

        var xml = CsvToXml.Convert(csv, "rows", "row");

        Assert.DoesNotContain("first name", xml);
        Assert.Contains("first-name", xml);
        // last-name was already valid (dashes allowed) — passes through unchanged
        Assert.Contains("last-name", xml);
    }

    [Fact]
    public void QuotedFieldsWithEmbeddedDelimitersAreParsedCorrectly()
    {
        var csv = "id,note\n1,\"hello, world\"\n2,\"a \"\"quoted\"\" word\"\n";

        var xml = CsvToXml.Convert(csv, "rows", "row");

        Assert.Contains("<note>hello, world</note>", xml);
        // SecurityElement.Escape encodes " as &quot;
        Assert.Contains("<note>a &quot;quoted&quot; word</note>", xml);
    }
}
