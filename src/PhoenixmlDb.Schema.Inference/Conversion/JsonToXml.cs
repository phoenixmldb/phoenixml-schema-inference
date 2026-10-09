using System.Text;
using System.Text.Json;
using PhoenixmlDb.Schema.Inference.Internal;

namespace PhoenixmlDb.Schema.Inference.Conversion;

/// <summary>
/// Converts JSON to XML using a deterministic mapping: objects become elements,
/// arrays become repeated child elements, primitives become text content, and
/// null becomes an empty element with <c>xsi:nil="true"</c>. Type information is
/// intentionally lost — recover it by inferring a schema from the JSON
/// (<c>SchemaGenerator.FromJson</c>), then use <c>XmlToJson</c> with that schema
/// for the reverse direction.
/// </summary>
public static class JsonToXml
{
    /// <summary>
    /// Converts <paramref name="json"/> to XML wrapped in a root element named
    /// <paramref name="rootElement"/>.
    /// </summary>
    public static string Convert(string json, string rootElement = "root")
    {
        var body = new StringBuilder();
        using var doc = JsonDocument.Parse(json);
        ConvertElement(doc.RootElement, body, "  ", null);

        // xsi is declared only when a null is written, so other documents are as before.
        var hasNil = body.ToString().Contains(Nil, StringComparison.Ordinal);
        var rootNil = doc.RootElement.ValueKind == JsonValueKind.Null;
        var sb = new StringBuilder();
        sb.Append('<').Append(rootElement);
        if (hasNil || rootNil)
            sb.Append(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"");
        if (rootNil)
            sb.Append(Nil);
        if (body.Length == 0)
        {
            // Nothing inside, and no whitespace either: an empty content model accepts none.
            sb.AppendLine("/>");
            return sb.ToString();
        }
        sb.AppendLine(">");
        sb.Append(body);
        sb.AppendLine($"</{rootElement}>");

        return sb.ToString();
    }

    private const string Nil = " xsi:nil=\"true\"";

    /// <summary>
    /// For a value written as an element with nothing inside, the attributes of that element:
    /// null is xsi:nil, an object with no members is just empty. Null for any other value.
    /// </summary>
    private static string? EmptyElement(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => Nil,
        JsonValueKind.Object when !value.EnumerateObject().Any() => string.Empty,
        _ => null,
    };

    private static void ConvertElement(
        JsonElement element, StringBuilder sb, string indent, string? elementName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var name = XmlNameSanitizer.Sanitize(prop.Name);

                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (EmptyElement(item) is { } attributes)
                            {
                                sb.AppendLine($"{indent}<{name}{attributes}/>");
                                continue;
                            }
                            sb.Append($"{indent}<{name}>");
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                sb.AppendLine();
                                ConvertElement(item, sb, indent + "  ", name);
                                sb.Append(indent);
                            }
                            else
                            {
                                sb.Append(XmlNameSanitizer.EscapeXml(LexicalValue(item)));
                            }
                            sb.AppendLine($"</{name}>");
                        }
                    }
                    else if (EmptyElement(prop.Value) is { } attributes)
                    {
                        sb.AppendLine($"{indent}<{name}{attributes}/>");
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        sb.AppendLine($"{indent}<{name}>");
                        ConvertElement(prop.Value, sb, indent + "  ", name);
                        sb.AppendLine($"{indent}</{name}>");
                    }
                    else
                    {
                        var value = XmlNameSanitizer.EscapeXml(LexicalValue(prop.Value));
                        sb.AppendLine($"{indent}<{name}>{value}</{name}>");
                    }
                }
                break;

            case JsonValueKind.Array:
                var itemName = elementName ?? "item";
                foreach (var item in element.EnumerateArray())
                {
                    if (EmptyElement(item) is { } attributes)
                    {
                        sb.AppendLine($"{indent}<{itemName}{attributes}/>");
                        continue;
                    }
                    sb.Append($"{indent}<{itemName}>");
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        sb.AppendLine();
                        ConvertElement(item, sb, indent + "  ", itemName);
                        sb.Append(indent);
                    }
                    else
                    {
                        sb.Append(XmlNameSanitizer.EscapeXml(LexicalValue(item)));
                    }
                    sb.AppendLine($"</{itemName}>");
                }
                break;

            default:
                sb.Append(XmlNameSanitizer.EscapeXml(LexicalValue(element)));
                break;
        }
    }

    private static string LexicalValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.ToString()
    };
}
