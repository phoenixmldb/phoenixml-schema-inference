using System.Text;
using System.Text.Json;
using PhoenixmlDb.Schema.Inference.Internal;

namespace PhoenixmlDb.Schema.Inference.Conversion;

/// <summary>
/// Converts JSON to XML using a deterministic mapping: objects become elements,
/// arrays become repeated child elements, primitives become text content. Type
/// information is intentionally lost — recover it by running schema inference
/// on the output, then use <c>XmlToJson</c> with the inferred schema for the
/// reverse direction.
/// </summary>
public static class JsonToXml
{
    /// <summary>
    /// Converts <paramref name="json"/> to XML wrapped in a root element named
    /// <paramref name="rootElement"/>.
    /// </summary>
    public static string Convert(string json, string rootElement = "root")
    {
        var sb = new StringBuilder();
        using var doc = JsonDocument.Parse(json);

        sb.AppendLine($"<{rootElement}>");
        ConvertElement(doc.RootElement, sb, "  ", null);
        sb.AppendLine($"</{rootElement}>");

        return sb.ToString();
    }

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
                    else if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        sb.AppendLine($"{indent}<{name}>");
                        ConvertElement(prop.Value, sb, indent + "  ", name);
                        sb.AppendLine($"{indent}</{name}>");
                    }
                    else
                    {
                        var value = prop.Value.ValueKind == JsonValueKind.Null
                            ? "" : XmlNameSanitizer.EscapeXml(LexicalValue(prop.Value));
                        sb.AppendLine($"{indent}<{name}>{value}</{name}>");
                    }
                }
                break;

            case JsonValueKind.Array:
                var itemName = elementName ?? "item";
                foreach (var item in element.EnumerateArray())
                {
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
