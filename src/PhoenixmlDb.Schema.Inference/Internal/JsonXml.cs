using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PhoenixmlDb.Schema.Inference.Internal;

/// <summary>
/// The XML form of JSON that <c>fn:json-to-xml</c> writes (XPath and XQuery Functions and
/// Operators 3.1, section 17.4): <c>map</c>, <c>array</c>, <c>string</c>, <c>number</c>,
/// <c>boolean</c> and <c>null</c> elements in the namespace
/// <c>http://www.w3.org/2005/xpath-functions</c>, a member's key in its <c>key</c> attribute.
/// </summary>
internal static class JsonXml
{
    private static readonly XNamespace Fn = "http://www.w3.org/2005/xpath-functions";

    /// <summary>Writes a JSON value in the XML form.</summary>
    public static XElement FromJson(JsonElement value, string? key = null)
    {
        var element = value.ValueKind switch
        {
            JsonValueKind.Object => new XElement(Fn + "map", value.EnumerateObject().Select(p => FromJson(p.Value, p.Name))),
            JsonValueKind.Array => new XElement(Fn + "array", value.EnumerateArray().Select(i => FromJson(i))),
            JsonValueKind.String => new XElement(Fn + "string", value.GetString()),
            JsonValueKind.Number => new XElement(Fn + "number", value.GetRawText()),
            JsonValueKind.True => new XElement(Fn + "boolean", "true"),
            JsonValueKind.False => new XElement(Fn + "boolean", "false"),
            _ => new XElement(Fn + "null"),
        };
        if (key is not null)
            element.SetAttributeValue("key", key);
        return element;
    }

    /// <summary>Reads the XML form back to JSON text.</summary>
    /// <exception cref="FormatException">The element is not in the XML form of JSON.</exception>
    public static string ToJson(XElement root)
    {
        var json = new StringBuilder();
        Write(root, json);
        return json.ToString();
    }

    private static void Write(XElement element, StringBuilder json)
    {
        if (element.Name.Namespace != Fn)
            throw new FormatException($"'{element.Name}' is not an element of the XML form of JSON.");
        switch (element.Name.LocalName)
        {
            case "map":
                json.Append('{');
                var first = true;
                foreach (var member in element.Elements())
                {
                    if (!first) json.Append(',');
                    first = false;
                    var key = (string?)member.Attribute("key")
                        ?? throw new FormatException("A member of a map has no key attribute.");
                    json.Append(Text(key, (bool?)member.Attribute("escaped-key") == true)).Append(':');
                    Write(member, json);
                }
                json.Append('}');
                break;
            case "array":
                json.Append('[');
                var firstItem = true;
                foreach (var item in element.Elements())
                {
                    if (!firstItem) json.Append(',');
                    firstItem = false;
                    Write(item, json);
                }
                json.Append(']');
                break;
            case "string":
                json.Append(Text(element.Value, (bool?)element.Attribute("escaped") == true));
                break;
            case "number":
                json.Append(element.Value.Trim());
                break;
            case "boolean":
                json.Append(element.Value.Trim() is "true" or "1" ? "true" : "false");
                break;
            case "null":
                json.Append("null");
                break;
            default:
                throw new FormatException($"'{element.Name.LocalName}' is not an element of the XML form of JSON.");
        }
    }

    /// <summary>
    /// A JSON string literal. Escaped text already holds JSON escapes; only what must still be
    /// escaped to stand in a literal is added.
    /// </summary>
    private static string Text(string value, bool escaped)
    {
        if (!escaped)
            return JsonSerializer.Serialize(value);
        var literal = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c == '"') literal.Append("\\\"");
            else if (c < ' ') literal.Append("\\u").Append(((int)c).ToString("x4"));
            else literal.Append(c);
        }
        return literal.Append('"').ToString();
    }
}
