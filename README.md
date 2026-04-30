# PhoenixmlDb.Schema.Inference

Open-source XSD inference and JSON/CSV/XML conversion for .NET. Apache-2.0.

Part of the [Phoenixml](https://phoenixml.dev) family.

## What it does

- **Infers XSD schemas from XML documents.** Observe one document or many; the
  inferred schema gets sharper with more samples (elements seen in every doc
  become required; numeric values produce `xs:decimal`; dates produce `xs:date`).
- **Converts JSON ↔ XML and CSV → XML** using deterministic mappings that round-trip
  cleanly when paired with the inferred schema.
- **Schema-aware `XmlToJson`** restores JSON types (numbers, booleans, dates) from
  the inferred schema on the way out.

## Quick start

```csharp
using PhoenixmlDb.Schema.Inference;

// XML in
var result = SchemaGenerator.FromXml(xmlContent);
Console.WriteLine(result.Xsd);

// JSON in
var result = SchemaGenerator.FromJson(jsonContent, rootElement: "orders");

// JSON round-trip with type recovery
var roundTrippedJson = XmlToJson.Convert(result.Xml, result.Xsd);
```

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
