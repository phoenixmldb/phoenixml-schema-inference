using System.Security;
using System.Text;

namespace PhoenixmlDb.Schema.Inference.Internal;

/// <summary>
/// Sanitizes arbitrary strings (e.g., JSON property names) into valid XML element
/// names, and escapes XML text content.
/// </summary>
internal static class XmlNameSanitizer
{
    public static string Sanitize(string name)
    {
        name = name.Trim();
        if (name.StartsWith('@')) name = name[1..];

        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('-');
        }

        var result = sb.ToString();
        if (result.Length == 0 || !char.IsLetter(result[0]))
            result = "field-" + result;

        return result;
    }

    public static string EscapeXml(string value) =>
        SecurityElement.Escape(value) ?? value;
}
