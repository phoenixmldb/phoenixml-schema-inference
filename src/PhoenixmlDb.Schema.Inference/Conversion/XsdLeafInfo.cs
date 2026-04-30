namespace PhoenixmlDb.Schema.Inference.Conversion;

/// <summary>
/// Carries the XSD-declared type information for a leaf (or complex) element,
/// used internally by <see cref="XmlToJson"/> to drive schema-aware type recovery.
/// </summary>
/// <param name="TypeName">
/// The qualified type name from the XSD <c>type=</c> attribute, e.g.
/// <c>"xs:integer"</c>, <c>"xs:string"</c>. <see langword="null"/> when the
/// element has a complex type or no type declaration.
/// </param>
/// <param name="IsArray">
/// <see langword="true"/> when the XSD declared <c>maxOccurs &gt; 1</c> or
/// <c>maxOccurs="unbounded"</c>; JSON ALWAYS emits an array for such elements
/// regardless of runtime occurrence count.
/// </param>
internal sealed record XsdLeafInfo(string? TypeName, bool IsArray);
