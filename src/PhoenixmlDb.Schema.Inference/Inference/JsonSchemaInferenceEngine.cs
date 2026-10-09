using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PhoenixmlDb.Schema.Inference.Internal;

namespace PhoenixmlDb.Schema.Inference.Inference;

/// <summary>
/// Infers a schema from JSON documents, from the JSON itself and not from an XML rendering of
/// it. What JSON says outright is kept: an array is an array when it has one item or none, a
/// number is a number and a string that looks like one is a string, and null is not the empty
/// string.
/// </summary>
/// <remarks>
/// <para>
/// Two schemas can be generated from what was observed. <see cref="GenerateXsd"/> describes the
/// element-per-key XML that <see cref="Conversion.JsonToXml"/> writes, and types every key on
/// its own. <see cref="GenerateJsonXmlXsd"/> describes the XML form of JSON that
/// <c>fn:json-to-xml</c> writes (<c>map</c>, <c>array</c>, <c>string</c>, <c>number</c>,
/// <c>boolean</c>, <c>null</c> with the key in an attribute). XSD 1.0 cannot give an element a
/// type that depends on the value of its <c>key</c> attribute, so that schema checks kinds, the
/// keys that may appear and that a key is not repeated, and no more.
/// </para>
/// <para>
/// A document is accepted as JSON text (<see cref="Observe"/>) or in that XML form
/// (<see cref="ObserveJsonXml"/>), which is how a store that keeps JSON as XML holds it.
/// </para>
/// </remarks>
public sealed partial class JsonSchemaInferenceEngine
{
    internal static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    internal static readonly XNamespace Fn = "http://www.w3.org/2005/xpath-functions";

    /// <summary>
    /// The namespace of the one annotation this engine writes on a declaration: <c>array="true"</c>
    /// on an element that is a JSON array where the schema cannot say so with maxOccurs.
    /// A schema processor ignores it; <see cref="Conversion.XmlToJson"/> reads it.
    /// </summary>
    internal static readonly XNamespace Hint = "urn:phoenixmldb:schema-inference";

    private readonly JsonNode _root = new();
    private readonly SortedSet<string> _keys = new(StringComparer.Ordinal);

    /// <summary>The number of documents observed.</summary>
    public int DocumentsObserved { get; private set; }

    /// <summary>Observes one JSON document.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public void Observe(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        ObserveValue(_root, document.RootElement);
        DocumentsObserved++;
    }

    /// <summary>
    /// Observes one JSON document in the XML form <c>fn:json-to-xml</c> writes.
    /// </summary>
    /// <exception cref="XmlException">The text is not XML.</exception>
    /// <exception cref="FormatException">The document is not in that form.</exception>
    public void ObserveJsonXml(string jsonXml)
    {
        ArgumentNullException.ThrowIfNull(jsonXml);
        var document = XDocument.Parse(jsonXml);
        // Through JSON text, so both entries observe exactly the same thing.
        Observe(JsonXml.ToJson(document.Root ?? throw new FormatException("The document has no root element.")));
    }

    private void ObserveValue(JsonNode node, JsonElement value)
    {
        node.Occurrences++;
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                node.Kinds |= JsonKinds.Object;
                node.Objects++;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    _keys.Add(property.Name);
                    if (!node.Members.TryGetValue(property.Name, out var member))
                    {
                        member = new JsonNode();
                        node.Members[property.Name] = member;
                        node.MemberOrder.Add(property.Name);
                    }
                    // A key written twice in one object: the first is the one counted as present.
                    if (seen.Add(property.Name))
                        member.PresentIn++;
                    ObserveValue(member, property.Value);
                }
                break;

            case JsonValueKind.Array:
                node.Kinds |= JsonKinds.Array;
                node.Item ??= new JsonNode();
                var length = value.GetArrayLength();
                node.MinItems = node.Arrays == 0 ? length : Math.Min(node.MinItems, length);
                node.MaxItems = Math.Max(node.MaxItems, length);
                node.Arrays++;
                foreach (var item in value.EnumerateArray())
                    ObserveValue(node.Item, item);
                break;

            case JsonValueKind.String:
                node.Kinds |= JsonKinds.String;
                var text = value.GetString() ?? string.Empty;
                node.Strings = node.Strings.Refine(text);
                node.AddSample(text);
                break;

            case JsonValueKind.Number:
                node.Kinds |= JsonKinds.Number;
                var lexical = value.GetRawText();
                node.Numbers = (NumberShape)Math.Max((int)node.Numbers, (int)ShapeOf(lexical));
                node.AddSample(lexical);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                node.Kinds |= JsonKinds.Boolean;
                node.AddSample(value.ValueKind == JsonValueKind.True ? "true" : "false");
                break;

            default:
                node.Kinds |= JsonKinds.Null;
                break;
        }
    }

    private static NumberShape ShapeOf(string lexical) =>
        lexical.AsSpan().IndexOfAny('e', 'E') >= 0 ? NumberShape.Double
        : lexical.Contains('.', StringComparison.Ordinal) ? NumberShape.Decimal
        : NumberShape.Integer;

    // ---------------------------------------------------------------------------------------
    // The summary
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What was observed, by JSON path. It does not depend on either XML form.
    /// </summary>
    public JsonInferenceSummary GetSummary()
    {
        var members = new List<JsonMemberSummary>();
        if (_root.Occurrences > 0)
            Summarise(_root, "$", required: true, members);
        return new JsonInferenceSummary { DocumentsObserved = DocumentsObserved, Members = members };
    }

    private static void Summarise(JsonNode node, string path, bool required, List<JsonMemberSummary> into)
    {
        into.Add(new JsonMemberSummary
        {
            Path = path,
            Kinds = node.Kinds,
            ValueType = node.ScalarType(),
            IsRequired = required,
            IsNullable = node.Kinds.HasFlag(JsonKinds.Null),
            Occurrences = node.Occurrences,
            MinItems = node.Arrays > 0 ? node.MinItems : null,
            MaxItems = node.Arrays > 0 ? node.MaxItems : null,
            SampleValues = [.. node.Samples],
        });
        foreach (var key in node.MemberOrder)
        {
            var member = node.Members[key];
            Summarise(member, path + (IsPlainKey(key) ? "." + key : "[" + JsonSerializer.Serialize(key) + "]"),
                member.PresentIn == node.Objects, into);
        }
        if (node.Item is { Occurrences: > 0 } item)
            Summarise(item, path + "[*]", required: node.MinItems > 0, into);
    }

    private static bool IsPlainKey(string key) => PlainKey().IsMatch(key);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainKey();

    // ---------------------------------------------------------------------------------------
    // The schema for element-per-key XML
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Generates the schema for the XML that <see cref="Conversion.JsonToXml"/> writes for the
    /// observed documents under a root element named <paramref name="rootElement"/>.
    /// </summary>
    /// <remarks>
    /// A key that every observed object has is required; a key seen with null is nillable; an
    /// array repeats. The members of an object are in any order, as they are in JSON. Where no
    /// member repeats that is an <c>xs:all</c>, which also checks that a required member is
    /// there and that none is written twice. XSD 1.0 has no such group with a repeating member,
    /// so an object that has one is a repeated choice of its members: any order, each typed,
    /// but a required member that is missing is not noticed. The summary still says which are
    /// required.
    /// </remarks>
    public string GenerateXsd(string rootElement = "root", string? targetNamespace = null)
    {
        ArgumentNullException.ThrowIfNull(rootElement);
        var schema = new XElement(Xs + "schema", new XAttribute(XNamespace.Xmlns + "xs", Xs.NamespaceName));
        if (targetNamespace is not null)
        {
            schema.Add(new XAttribute("targetNamespace", targetNamespace),
                new XAttribute(XNamespace.Xmlns + "tns", targetNamespace),
                new XAttribute("elementFormDefault", "qualified"));
        }
        schema.Add(Documentation($"Auto-generated by PhoenixmlDb.Schema.Inference from {DocumentsObserved} JSON document(s). "
            + "Refine this schema to add constraints: restrict enumerations, tighten types."));
        if (_root.Occurrences == 0)
            return schema.ToString();

        var root = new XElement(Xs + "element", new XAttribute("name", rootElement));
        if (_root.Kinds.HasFlag(JsonKinds.Array))
        {
            // A top-level array: its items are written as <item> elements.
            var items = ElementView.Of(_root).Item!;
            root.Add(new XElement(Xs + "complexType",
                new XElement(Xs + "sequence", Declare("item", items, required: false, repeats: true))));
        }
        else
        {
            Describe(root, ElementView.Of(_root));
        }
        schema.Add(root);
        return schema.ToString();
    }

    private static XElement Declare(string name, ElementView view, bool required, bool repeats)
    {
        var element = new XElement(Xs + "element", new XAttribute("name", name));
        if (!required)
            element.Add(new XAttribute("minOccurs", "0"));
        if (repeats)
            element.Add(new XAttribute("maxOccurs", "unbounded"));
        Describe(element, view);
        return element;
    }

    /// <summary>Gives <paramref name="element"/> its type, from what the values under it were.</summary>
    private static void Describe(XElement element, ElementView view)
    {
        var kinds = view.Kinds & ~JsonKinds.Null;
        if (view.Kinds.HasFlag(JsonKinds.Null))
            element.Add(new XAttribute("nillable", "true"));

        if (view.NestedArrays)
        {
            // JsonToXml writes an array inside an array as its JSON text.
            element.Add(new XAttribute("type", "xs:string"),
                Documentation("An array inside an array: its items are written as JSON text."));
            return;
        }
        if (kinds == JsonKinds.Object)
        {
            var type = new XElement(Xs + "complexType");
            var repeating = view.Members.Any(m => m.Repeats);
            var group = repeating
                ? new XElement(Xs + "choice", new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                : new XElement(Xs + "all");
            foreach (var member in view.Members)
            {
                // In the choice, how often a member appears is the choice's to say.
                if (!repeating)
                {
                    group.Add(Declare(member.Name, member.View, member.Required, member.Repeats));
                    continue;
                }
                var declared = Declare(member.Name, member.View, required: true, repeats: false);
                if (member.Repeats)
                {
                    declared.Add(new XAttribute(XNamespace.Xmlns + "i", Hint.NamespaceName), new XAttribute(Hint + "array", "true"));
                }
                group.Add(declared);
            }
            var mustHave = view.Members.Where(m => m.Required).Select(m => m.Name).ToList();
            if (repeating && mustHave.Count > 0)
            {
                element.Add(Documentation("A member repeats, so the members are a repeated choice and these required ones are not checked: "
                    + string.Join(", ", mustHave)));
            }
            type.Add(group);
            element.Add(type);
            return;
        }
        if (kinds.HasFlag(JsonKinds.Object))
        {
            // An object in some documents and a plain value in others.
            element.Add(new XAttribute("type", "xs:anyType"),
                Documentation("Observed both as an object and as a plain value."));
            return;
        }
        element.Add(new XAttribute("type", view.ScalarType ?? "xs:string"));
        if (view.Samples.Count > 0)
            element.AddFirst(Documentation("Samples: " + string.Join(", ", view.Samples.Take(3))));
    }

    /// <summary>
    /// The observed documents as the elements of element-per-key XML, in the shape
    /// <see cref="SchemaInferenceSummary"/> has always had.
    /// </summary>
    public SchemaInferenceSummary GetElementSummary(string rootElement = "root")
    {
        ArgumentNullException.ThrowIfNull(rootElement);
        var elements = new List<InferredElementSummary>();
        if (_root.Occurrences > 0)
        {
            var view = ElementView.Of(_root);
            Add(rootElement, rootElement, view, required: true, repeats: false, rate: 1, elements);
            if (view.Item is { } items && _root.Kinds.HasFlag(JsonKinds.Array))
                Add(rootElement + "/item", "item", items, required: false, repeats: true, rate: 1, elements);
        }
        return new SchemaInferenceSummary
        {
            DocumentsObserved = DocumentsObserved,
            Elements = [.. elements.OrderBy(e => e.Path, StringComparer.Ordinal)],
        };
    }

    private static void Add(string path, string name, ElementView view, bool required, bool repeats, double rate,
        List<InferredElementSummary> into)
    {
        var kinds = view.Kinds & ~JsonKinds.Null;
        into.Add(new InferredElementSummary
        {
            Path = path,
            Name = name,
            InferredType = kinds.HasFlag(JsonKinds.Object) ? "Complex" : (view.ScalarType ?? "xs:string")["xs:".Length..] switch
            {
                "string" => "String",
                "integer" => "Integer",
                "decimal" => "Decimal",
                "double" => "Double",
                "boolean" => "Boolean",
                "date" => "Date",
                _ => "DateTime",
            },
            IsRequired = required,
            OccurrenceRate = rate,
            IsArray = repeats,
            SampleValues = [.. view.Samples.Take(3)],
        });
        foreach (var member in view.Members)
            Add(path + "/" + member.Name, member.Name, member.View, member.Required, member.Repeats, member.Rate, into);
    }

    internal static XElement Documentation(string text) =>
        new(Xs + "annotation", new XElement(Xs + "documentation", text));

    /// <summary>
    /// A node as element-per-key XML shows it: an array and its items are the same repeating
    /// element, and keys that become the same element name are one element.
    /// </summary>
    private sealed class ElementView
    {
        public JsonKinds Kinds { get; private set; }
        public string? ScalarType { get; private set; }
        public bool NestedArrays { get; private set; }
        public List<string> Samples { get; } = [];
        public List<(string Name, ElementView View, bool Required, bool Repeats, double Rate)> Members { get; } = [];
        public ElementView? Item { get; private set; }

        public static ElementView Of(JsonNode node)
        {
            var view = new ElementView { Kinds = node.Kinds & ~JsonKinds.Array, ScalarType = node.ScalarType() };
            view.Samples.AddRange(node.Samples);

            var byName = new Dictionary<string, List<(string Key, JsonNode Node)>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var key in node.MemberOrder)
            {
                var name = XmlNameSanitizer.Sanitize(key);
                if (!byName.TryGetValue(name, out var sharing))
                {
                    byName[name] = sharing = [];
                    order.Add(name);
                }
                sharing.Add((key, node.Members[key]));
            }
            foreach (var name in order)
            {
                var sharing = byName[name];
                var merged = sharing.Count == 1 ? sharing[0].Node : JsonNode.Merge(sharing.Select(s => s.Node));
                var isArray = merged.Kinds.HasFlag(JsonKinds.Array);
                // An array's items are the element; the array itself is not written.
                var content = isArray ? JsonNode.Merge([merged.WithoutArray(), merged.Item!]) : merged;
                var memberView = Of(content);
                if (isArray && merged.Item!.Kinds.HasFlag(JsonKinds.Array))
                    memberView.NestedArrays = true;
                var inEveryObject = sharing.Any(s => s.Node.PresentIn == node.Objects);
                // An empty array writes no element, and so does a key that is absent.
                var required = inEveryObject && (!isArray || merged.MinItems > 0);
                var rate = node.Objects == 0 ? 0 : Math.Min(1.0, (double)sharing.Max(m => m.Node.PresentIn) / node.Objects);
                view.Members.Add((name, memberView, required, isArray || sharing.Count > 1, rate));
            }
            if (node.Item is { } item)
            {
                view.Item = Of(item);
                if (item.Kinds.HasFlag(JsonKinds.Array))
                    view.Item.NestedArrays = true;
            }
            return view;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The schema for the XML form of JSON
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Generates a schema for the observed documents in the XML form <c>fn:json-to-xml</c>
    /// writes. It checks the kind of the root, the kinds that appear inside a map and inside an
    /// array, the lexical form of numbers, the keys that may appear, and that no map repeats a
    /// key. It does not check which key has which type: XSD 1.0 cannot say that.
    /// </summary>
    public string GenerateJsonXmlXsd()
    {
        var kinds = new KindsSeen();
        if (_root.Occurrences > 0)
            kinds.Collect(_root, inMap: false, inArray: false);

        var schema = new XElement(Xs + "schema",
            new XAttribute(XNamespace.Xmlns + "xs", Xs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "j", Fn.NamespaceName),
            new XAttribute("targetNamespace", Fn.NamespaceName),
            new XAttribute("elementFormDefault", "qualified"),
            Documentation($"Inferred by PhoenixmlDb.Schema.Inference from {DocumentsObserved} JSON document(s), "
                + "for the XML form of JSON. Kinds, keys and key uniqueness are checked; the type of each key is not."));

        // The root: one global element for each kind a document was.
        foreach (var kind in KindsSeen.Order.Where(k => _root.Kinds.HasFlag(k)))
            schema.Add(KindElement(kind, keyed: false, global: true));

        schema.Add(new XElement(Xs + "complexType", new XAttribute("name", "map"),
            Members(kinds.InMap, keyed: true)));
        schema.Add(new XElement(Xs + "complexType", new XAttribute("name", "array"),
            Members(kinds.InArray, keyed: false)));

        var key = new XElement(Xs + "restriction", new XAttribute("base", "xs:string"));
        foreach (var name in _keys)
            key.Add(new XElement(Xs + "enumeration", new XAttribute("value", name)));
        if (_keys.Count == 0)
            key.Add(new XElement(Xs + "maxLength", new XAttribute("value", "0")));
        schema.Add(new XElement(Xs + "simpleType", new XAttribute("name", "key"), key));

        schema.Add(new XElement(Xs + "simpleType", new XAttribute("name", "number"),
            new XElement(Xs + "restriction", new XAttribute("base", kinds.Numbers switch
            {
                NumberShape.Integer => "xs:integer",
                NumberShape.Decimal => "xs:decimal",
                _ => "xs:double",
            }))));
        return schema.ToString();
    }

    private static XElement Members(JsonKinds kinds, bool keyed)
    {
        var choice = new XElement(Xs + "choice", new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"));
        foreach (var kind in KindsSeen.Order.Where(k => kinds.HasFlag(k)))
            choice.Add(KindElement(kind, keyed, global: false));
        return choice;
    }

    private static XElement KindElement(JsonKinds kind, bool keyed, bool global)
    {
        var name = kind switch
        {
            JsonKinds.Object => "map",
            JsonKinds.Array => "array",
            JsonKinds.String => "string",
            JsonKinds.Number => "number",
            JsonKinds.Boolean => "boolean",
            _ => "null",
        };
        var element = new XElement(Xs + "element", new XAttribute("name", name));
        var type = new XElement(Xs + "complexType");
        XElement attributesGoOn = type;
        switch (kind)
        {
            case JsonKinds.Object:
                attributesGoOn = Extension(type, "j:map", simple: false);
                // No two members of a map have the same key.
                element.Add(type, new XElement(Xs + "unique", new XAttribute("name", "key" + (global ? "-root" : keyed ? "-in-map" : "-in-array")),
                    new XElement(Xs + "selector", new XAttribute("xpath", "*")),
                    new XElement(Xs + "field", new XAttribute("xpath", "@key"))));
                break;
            case JsonKinds.Array:
                attributesGoOn = Extension(type, "j:array", simple: false);
                element.Add(type);
                break;
            case JsonKinds.Null:
                element.Add(type);
                break;
            default:
                attributesGoOn = Extension(type, kind == JsonKinds.Number ? "j:number" : kind == JsonKinds.Boolean ? "xs:boolean" : "xs:string", simple: true);
                element.Add(type);
                break;
        }
        if (keyed)
        {
            attributesGoOn.Add(new XElement(Xs + "attribute", new XAttribute("name", "key"), new XAttribute("type", "j:key"), new XAttribute("use", "required")),
                new XElement(Xs + "attribute", new XAttribute("name", "escaped-key"), new XAttribute("type", "xs:boolean")));
        }
        if (kind == JsonKinds.String)
            attributesGoOn.Add(new XElement(Xs + "attribute", new XAttribute("name", "escaped"), new XAttribute("type", "xs:boolean")));
        return element;
    }

    private static XElement Extension(XElement complexType, string baseType, bool simple)
    {
        var extension = new XElement(Xs + "extension", new XAttribute("base", baseType));
        complexType.Add(new XElement(Xs + (simple ? "simpleContent" : "complexContent"), extension));
        return extension;
    }

    private sealed class KindsSeen
    {
        public static readonly JsonKinds[] Order =
            [JsonKinds.Object, JsonKinds.Array, JsonKinds.String, JsonKinds.Number, JsonKinds.Boolean, JsonKinds.Null];

        public JsonKinds InMap { get; private set; }
        public JsonKinds InArray { get; private set; }
        public NumberShape Numbers { get; private set; }

        public void Collect(JsonNode node, bool inMap, bool inArray)
        {
            if (inMap) InMap |= node.Kinds;
            if (inArray) InArray |= node.Kinds;
            Numbers = (NumberShape)Math.Max((int)Numbers, (int)node.Numbers);
            foreach (var member in node.Members.Values)
                Collect(member, inMap: true, inArray: false);
            if (node.Item is { Occurrences: > 0 } item)
                Collect(item, inMap: false, inArray: true);
        }
    }
}
