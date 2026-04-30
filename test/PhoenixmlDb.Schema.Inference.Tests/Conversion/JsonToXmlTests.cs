using PhoenixmlDb.Schema.Inference.Conversion;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.Conversion;

public class JsonToXmlTests
{
    [Fact]
    public void ObjectBecomesElementWithChildren()
    {
        var json = """{ "id": "ORD-1", "total": 1500.00 }""";

        var xml = JsonToXml.Convert(json, "order");

        Assert.Contains("<order>", xml);
        Assert.Contains("<id>ORD-1</id>", xml);
        Assert.Contains("<total>1500", xml);
        Assert.Contains("</order>", xml);
    }

    [Fact]
    public void ArrayPropertyBecomesRepeatedElements()
    {
        var json = """{ "items": [ { "name": "A" }, { "name": "B" } ] }""";

        var xml = JsonToXml.Convert(json, "doc");

        Assert.Equal(2, CountOccurrences(xml, "<items>"));
        Assert.Contains("<name>A</name>", xml);
        Assert.Contains("<name>B</name>", xml);
    }

    [Fact]
    public void InvalidXmlNameCharsInKeysAreSanitized()
    {
        // Sanitizer rules from ConduitXP source:
        //  - leading "@" stripped
        //  - spaces become dashes
        //  - non-alphanumeric/dash/underscore chars dropped
        //  - if result is empty or doesn't start with a letter, "field-" is prepended
        var json = """{ "first name": "Alice", "@id": "x" }""";

        var xml = JsonToXml.Convert(json, "doc");

        Assert.DoesNotContain("first name", xml);
        Assert.Contains("first-name", xml);
        Assert.Contains("<id>x</id>", xml);
    }

    [Fact]
    public void NullValuesBecomeEmptyElements()
    {
        var json = """{ "missing": null }""";

        var xml = JsonToXml.Convert(json, "doc");

        Assert.Contains("<missing></missing>", xml);
    }

    [Fact]
    public void SpecialCharsInValuesAreEscaped()
    {
        var json = """{ "note": "a < b & c > d" }""";

        var xml = JsonToXml.Convert(json, "doc");

        Assert.Contains("&lt;", xml);
        Assert.Contains("&amp;", xml);
        Assert.Contains("&gt;", xml);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
