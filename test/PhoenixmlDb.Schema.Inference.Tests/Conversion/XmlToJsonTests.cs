using PhoenixmlDb.Schema.Inference.Conversion;
using Xunit;

namespace PhoenixmlDb.Schema.Inference.Tests.Conversion;

public class XmlToJsonTests
{
    [Fact]
    public void WithoutSchemaAllLeavesBecomeStrings()
    {
        var xml = "<order><id>ORD-1</id><total>1500.00</total></order>";

        var json = XmlToJson.Convert(xml, xsd: null);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var order = doc.RootElement.GetProperty("order");
        Assert.Equal("ORD-1", order.GetProperty("id").GetString());
        Assert.Equal("1500.00", order.GetProperty("total").GetString());  // string, not number
    }

    [Fact]
    public void WithSchemaDecimalElementBecomesJsonNumber()
    {
        var xml = "<order><id>ORD-1</id><total>1500.00</total></order>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="order">
                <xs:complexType><xs:sequence>
                  <xs:element name="id" type="xs:string"/>
                  <xs:element name="total" type="xs:decimal"/>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var order = doc.RootElement.GetProperty("order");
        Assert.Equal("ORD-1", order.GetProperty("id").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, order.GetProperty("total").ValueKind);
        Assert.Equal(1500.00m, order.GetProperty("total").GetDecimal());
    }

    [Fact]
    public void WithSchemaIntegerElementBecomesJsonNumber()
    {
        var xml = "<order><qty>10</qty></order>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="order">
                <xs:complexType><xs:sequence>
                  <xs:element name="qty" type="xs:integer"/>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(10L, doc.RootElement.GetProperty("order").GetProperty("qty").GetInt64());
    }

    [Fact]
    public void WithSchemaBooleanElementBecomesJsonBoolean()
    {
        var xml = "<order><active>true</active></order>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="order">
                <xs:complexType><xs:sequence>
                  <xs:element name="active" type="xs:boolean"/>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("order").GetProperty("active").GetBoolean());
    }

    [Fact]
    public void WithSchemaDateElementRemainsIso8601String()
    {
        var xml = "<order><date>2026-04-29</date></order>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="order">
                <xs:complexType><xs:sequence>
                  <xs:element name="date" type="xs:date"/>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("2026-04-29", doc.RootElement.GetProperty("order").GetProperty("date").GetString());
    }

    [Fact]
    public void TypeDisagreementFallsBackToString()
    {
        // XSD says integer; XML says "abc". Per design, emit as string, never throw.
        var xml = "<order><qty>abc</qty></order>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="order">
                <xs:complexType><xs:sequence>
                  <xs:element name="qty" type="xs:integer"/>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var qty = doc.RootElement.GetProperty("order").GetProperty("qty");
        Assert.Equal(System.Text.Json.JsonValueKind.String, qty.ValueKind);
        Assert.Equal("abc", qty.GetString());
    }

    [Fact]
    public void ArrayElementsAreAlwaysWrappedWhenSchemaSaysRepeating()
    {
        // Schema declares maxOccurs>1; runtime XML has one occurrence — JSON still emits an array.
        var xml = "<catalog><item><name>Widget</name></item></catalog>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="catalog">
                <xs:complexType><xs:sequence>
                  <xs:element name="item" maxOccurs="unbounded">
                    <xs:complexType><xs:sequence>
                      <xs:element name="name" type="xs:string"/>
                    </xs:sequence></xs:complexType>
                  </xs:element>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("catalog").GetProperty("item");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, items.ValueKind);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Widget", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ArrayElementsCollectMultipleOccurrences()
    {
        var xml = "<catalog><item><name>A</name></item><item><name>B</name></item></catalog>";
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="catalog">
                <xs:complexType><xs:sequence>
                  <xs:element name="item" maxOccurs="unbounded">
                    <xs:complexType><xs:sequence>
                      <xs:element name="name" type="xs:string"/>
                    </xs:sequence></xs:complexType>
                  </xs:element>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        var json = XmlToJson.Convert(xml, xsd);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("catalog").GetProperty("item");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("A", items[0].GetProperty("name").GetString());
        Assert.Equal("B", items[1].GetProperty("name").GetString());
    }

    [Fact]
    public void AttributesArePreservedWithAtPrefix()
    {
        var xml = "<order id=\"42\" priority=\"high\"><total>100</total></order>";

        var json = XmlToJson.Convert(xml, xsd: null);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var order = doc.RootElement.GetProperty("order");
        Assert.Equal("42", order.GetProperty("@id").GetString());
        Assert.Equal("high", order.GetProperty("@priority").GetString());
        Assert.Equal("100", order.GetProperty("total").GetString());
    }

    [Fact]
    public void EmptyElementBecomesEmptyObjectOrEmptyString()
    {
        // Decision: empty leaf element (no children, no text) → empty string in JSON.
        // (Document this convention in the class doc-comment too.)
        var xml = "<order><note></note></order>";

        var json = XmlToJson.Convert(xml, xsd: null);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("", doc.RootElement.GetProperty("order").GetProperty("note").GetString());
    }
}
