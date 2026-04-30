using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PhoenixmlDb.Schema.Inference.Conversion;

/// <summary>
/// Converts XML to JSON with optional schema-aware type recovery.
///
/// JSON is structurally weaker than XML: no attributes, no required cardinality,
/// no type declarations. This converter "assumes the worst" so the JSON output
/// is unambiguous to consumers:
///   * Elements declared with maxOccurs &gt; 1 in the XSD ALWAYS produce a JSON
///     array, even when only one occurrence is present at runtime.
///   * XML attributes are preserved as JSON object keys prefixed with '@'.
///   * Without an XSD, every leaf is a JSON string. With an XSD, xs:integer,
///     xs:decimal, xs:double, xs:float, and xs:boolean leaves become JSON
///     numbers/booleans; other typed leaves (xs:date, xs:dateTime, xs:anyURI,
///     etc.) remain ISO-encoded strings.
///   * If the runtime value can't be parsed as the declared XSD type, the leaf
///     falls back to a JSON string instead of throwing — the XSD describes the
///     contract, but JSON can't carry it, so failing loud here just punishes
///     the JSON consumer.
///   * Empty leaf elements (no children, no text) become empty JSON strings.
///
/// For lossless typed round-trip, prefer XML+XSD over JSON. This converter
/// exists for pipelines that require JSON output, not because JSON is the
/// better representation.
///
/// Limitation: only inline complexType definitions are walked. Named
/// complexType references (xs:complexType name="..." outside an element) are
/// not yet resolved; those leaves fall back to string.
/// </summary>
public static class XmlToJson
{
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    // Integer-family XSD types: try parse as long.
    private static readonly HashSet<string> IntegerTypes = new(StringComparer.Ordinal)
    {
        "xs:integer", "xs:int", "xs:long", "xs:short", "xs:byte",
        "xs:nonNegativeInteger", "xs:positiveInteger",
        "xs:unsignedInt", "xs:unsignedLong"
    };

    // Decimal/float-family: try parse as decimal (or double for double/float).
    private static readonly HashSet<string> DecimalTypes = new(StringComparer.Ordinal)
    {
        "xs:decimal", "xs:double", "xs:float"
    };

    // String-family: always emit as JSON string (no conversion attempt).
    private static readonly HashSet<string> StringTypes = new(StringComparer.Ordinal)
    {
        "xs:string", "xs:normalizedString", "xs:token", "xs:anyURI",
        "xs:date", "xs:dateTime", "xs:time", "xs:duration"
    };

    /// <summary>
    /// Converts <paramref name="xml"/> to JSON, optionally using <paramref name="xsd"/>
    /// for schema-aware type recovery.
    /// </summary>
    /// <param name="xml">The XML document string to convert.</param>
    /// <param name="xsd">
    /// An XSD schema string. When provided, element types declared in the schema
    /// are used to emit JSON numbers and booleans instead of strings. Pass
    /// <see langword="null"/> for no schema (all leaves become strings).
    /// </param>
    /// <returns>Indented JSON (2-space) representing the XML document.</returns>
    public static string Convert(string xml, string? xsd)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var xmlDoc = XDocument.Parse(xml);
        var typeMap = xsd is not null ? BuildTypeMap(xsd) : null;

        var sb = new StringBuilder();
        sb.AppendLine("{");
        EmitElement(xmlDoc.Root!, sb, "  ", typeMap, "/");
        sb.Append('}');
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // XSD type-map building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a map from element path (e.g. "/order/total") to its leaf type info
    /// by walking the XSD's inline xs:element declarations recursively.
    /// Named complexType references outside an element are not resolved (limitation).
    /// </summary>
    private static Dictionary<string, XsdLeafInfo> BuildTypeMap(string xsd)
    {
        var map = new Dictionary<string, XsdLeafInfo>(StringComparer.Ordinal);
        var schemaDoc = XDocument.Parse(xsd);

        foreach (var topElement in schemaDoc.Root?.Elements(Xs + "element") ?? [])
        {
            WalkElement(topElement, "/", map);
        }

        return map;
    }

    private static void WalkElement(
        XElement xsElement,
        string parentPath,
        Dictionary<string, XsdLeafInfo> map)
    {
        var name = (string?)xsElement.Attribute("name");
        if (name is null) return;

        var path = parentPath.EndsWith('/')
            ? parentPath + name
            : parentPath + "/" + name;

        var maxOccursAttr = (string?)xsElement.Attribute("maxOccurs");
        var isArray = maxOccursAttr is not null
            && (maxOccursAttr.Equals("unbounded", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(maxOccursAttr, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 1));

        // Check for a type= attribute (simple leaf).
        var typeAttr = (string?)xsElement.Attribute("type");

        // Look for inline complexType child.
        var inlineComplexType = xsElement.Element(Xs + "complexType");

        if (inlineComplexType is not null)
        {
            // Record the element itself (complex type, no leaf type name).
            map[path] = new XsdLeafInfo(null, isArray);

            // Recurse into xs:sequence, xs:all, xs:choice children.
            var sequences = inlineComplexType
                .Elements()
                .Where(e => e.Name == Xs + "sequence" || e.Name == Xs + "all" || e.Name == Xs + "choice");

            foreach (var seq in sequences)
            {
                foreach (var child in seq.Elements(Xs + "element"))
                {
                    WalkElement(child, path, map);
                }
            }
        }
        else
        {
            // Leaf element: record its declared type (may be null if no type= attr).
            map[path] = new XsdLeafInfo(typeAttr, isArray);
        }
    }

    // -------------------------------------------------------------------------
    // JSON emission
    // -------------------------------------------------------------------------

    private static void EmitElement(
        XElement element,
        StringBuilder sb,
        string indent,
        Dictionary<string, XsdLeafInfo>? typeMap,
        string parentPath)
    {
        var name = element.Name.LocalName;
        var path = parentPath.EndsWith('/')
            ? parentPath + name
            : parentPath + "/" + name;

        XsdLeafInfo? leafInfo = null;
        typeMap?.TryGetValue(path, out leafInfo);

        sb.Append(indent);
        sb.Append(JsonSerializer.Serialize(name));
        sb.Append(": ");

        EmitValue(element, sb, indent, typeMap, path, leafInfo);
    }

    private static void EmitValue(
        XElement element,
        StringBuilder sb,
        string indent,
        Dictionary<string, XsdLeafInfo>? typeMap,
        string path,
        XsdLeafInfo? leafInfo)
    {
        var hasChildren = element.Elements().Any();
        var hasAttributes = element.Attributes().Any();
        var text = element.HasElements ? null : element.Value;

        if (!hasChildren && !hasAttributes)
        {
            // Pure leaf: text content only.
            EmitLeaf(text ?? string.Empty, leafInfo?.TypeName, sb);
            return;
        }

        // Complex: object with attributes and/or child elements.
        sb.AppendLine("{");
        var childIndent = indent + "  ";

        // Attributes first, with "@" prefix.
        var attrs = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var totalProps = attrs.Count + CountDistinctChildKeys(children);
        var propIndex = 0;

        foreach (var attr in attrs)
        {
            sb.Append(childIndent);
            sb.Append(JsonSerializer.Serialize("@" + attr.Name.LocalName));
            sb.Append(": ");
            sb.Append(JsonSerializer.Serialize(attr.Value));
            propIndex++;
            if (propIndex < totalProps || (hasChildren && children.Count > 0))
                sb.AppendLine(",");
            else
                sb.AppendLine();
        }

        // Child elements — handle arrays (same-name repeating siblings).
        EmitChildElements(children, sb, childIndent, typeMap, path, attrs.Count, totalProps, propIndex);

        sb.Append(indent);
        sb.Append('}');
    }

    private static void EmitChildElements(
        List<XElement> children,
        StringBuilder sb,
        string indent,
        Dictionary<string, XsdLeafInfo>? typeMap,
        string parentPath,
        int attrCount,
        int totalProps,
        int propIndex)
    {
        // Group children by local name while preserving first-occurrence order.
        var order = new List<string>();
        var grouped = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);

        foreach (var child in children)
        {
            var localName = child.Name.LocalName;
            if (!grouped.TryGetValue(localName, out var bucket))
            {
                bucket = [];
                grouped[localName] = bucket;
                order.Add(localName);
            }
            bucket.Add(child);
        }

        var distinctKeys = order.Count;
        var keyIndex = 0;

        foreach (var childName in order)
        {
            var childPath = parentPath.EndsWith('/')
                ? parentPath + childName
                : parentPath + "/" + childName;

            XsdLeafInfo? childLeafInfo = null;
            typeMap?.TryGetValue(childPath, out childLeafInfo);

            var group = grouped[childName];
            var isLastKey = keyIndex == distinctKeys - 1;
            keyIndex++;

            sb.Append(indent);
            sb.Append(JsonSerializer.Serialize(childName));
            sb.Append(": ");

            // Emit as array if: schema says IsArray, OR there are multiple runtime occurrences.
            var schemaArray = childLeafInfo?.IsArray ?? false;
            var runtimeMultiple = group.Count > 1;

            if (schemaArray || runtimeMultiple)
            {
                sb.AppendLine("[");
                var arrayIndent = indent + "  ";
                for (var i = 0; i < group.Count; i++)
                {
                    var isLast = i == group.Count - 1;
                    sb.Append(arrayIndent);
                    EmitArrayItem(group[i], sb, arrayIndent, typeMap, childPath, childLeafInfo);
                    if (!isLast) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.Append(indent);
                sb.Append(']');
            }
            else
            {
                // Single occurrence, not schema-declared as array.
                var child = group[0];
                EmitArrayItem(child, sb, indent, typeMap, childPath, childLeafInfo);
            }

            if (!isLastKey) sb.AppendLine(",");
            else sb.AppendLine();
        }
    }

    private static void EmitArrayItem(
        XElement element,
        StringBuilder sb,
        string indent,
        Dictionary<string, XsdLeafInfo>? typeMap,
        string path,
        XsdLeafInfo? leafInfo)
    {
        var hasChildren = element.Elements().Any();
        var hasAttributes = element.Attributes().Any();

        if (!hasChildren && !hasAttributes)
        {
            var text = element.Value;
            EmitLeaf(text, leafInfo?.TypeName, sb);
        }
        else
        {
            EmitObjectBody(element, sb, indent, typeMap, path);
        }
    }

    private static void EmitObjectBody(
        XElement element,
        StringBuilder sb,
        string indent,
        Dictionary<string, XsdLeafInfo>? typeMap,
        string path)
    {
        sb.AppendLine("{");
        var childIndent = indent + "  ";

        var attrs = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var totalProps = attrs.Count + CountDistinctChildKeys(children);
        var propIndex = 0;

        foreach (var attr in attrs)
        {
            sb.Append(childIndent);
            sb.Append(JsonSerializer.Serialize("@" + attr.Name.LocalName));
            sb.Append(": ");
            sb.Append(JsonSerializer.Serialize(attr.Value));
            propIndex++;
            if (propIndex < totalProps)
                sb.AppendLine(",");
            else
                sb.AppendLine();
        }

        EmitChildElements(children, sb, childIndent, typeMap, path, attrs.Count, totalProps, propIndex);

        sb.Append(indent);
        sb.Append('}');
    }

    private static void EmitLeaf(string value, string? typeName, StringBuilder sb)
    {
        if (typeName is null || StringTypes.Contains(typeName))
        {
            sb.Append(JsonSerializer.Serialize(value));
            return;
        }

        if (typeName.Equals("xs:boolean", StringComparison.Ordinal))
        {
            // XSD canonical lexical space is lowercase "true"/"false", but be tolerant
            // of common alternates (capitalized, "1"/"0") on input.
            var trimmed = value.Trim();
            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
            {
                sb.Append("true");
                return;
            }
            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
            {
                sb.Append("false");
                return;
            }
            // Not a valid boolean — fall back to string.
            sb.Append(JsonSerializer.Serialize(value));
            return;
        }

        if (IntegerTypes.Contains(typeName))
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            {
                sb.Append(longVal.ToString(CultureInfo.InvariantCulture));
                return;
            }
            // Fall back to string.
            sb.Append(JsonSerializer.Serialize(value));
            return;
        }

        if (DecimalTypes.Contains(typeName))
        {
            // Use decimal for xs:decimal; double for xs:double and xs:float.
            if (typeName.Equals("xs:double", StringComparison.Ordinal)
                || typeName.Equals("xs:float", StringComparison.Ordinal))
            {
                if (double.TryParse(
                    value,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var dblVal))
                {
                    // Use R format to ensure round-trip accuracy.
                    sb.Append(dblVal.ToString("R", CultureInfo.InvariantCulture));
                    return;
                }
            }
            else
            {
                if (decimal.TryParse(
                    value,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var decVal))
                {
                    sb.Append(decVal.ToString(CultureInfo.InvariantCulture));
                    return;
                }
            }
            // Fall back to string.
            sb.Append(JsonSerializer.Serialize(value));
            return;
        }

        // Unrecognized type — fall back to string.
        sb.Append(JsonSerializer.Serialize(value));
    }

    private static int CountDistinctChildKeys(List<XElement> children) =>
        children.Select(e => e.Name.LocalName).Distinct(StringComparer.Ordinal).Count();
}
