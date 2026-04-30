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
  the inferred schema on the way out — JSON pipelines that need typed values
  without hand-writing the schema can drop this in.

## Why this exists

JSON is structurally weaker than XML — no attributes, no required cardinality,
no type declarations. This library treats XML+XSD as the better-typed
representation and gives JSON pipelines a way to participate without giving up
those guarantees: observe XML to infer an XSD, then use that XSD to recover
types when emitting JSON.

The conversion functions (`JsonToXml`, `CsvToXml`, `XmlToJson`) and the
inference engine were extracted from [ConduitXP](https://github.com/endpointsystems/conduitxp)'s
adapter framework so they can be consumed independently.

## Quick start

```csharp
using PhoenixmlDb.Schema.Inference.Generation;
using PhoenixmlDb.Schema.Inference.Conversion;

// XML in → XSD out
var result = SchemaGenerator.FromXml(xmlContent);
Console.WriteLine(result.Xsd);

// JSON in → XSD out (JSON is converted to XML internally)
var fromJson = SchemaGenerator.FromJson(jsonContent, rootElement: "orders");

// JSON round-trip with type recovery
var roundTrippedJson = XmlToJson.Convert(fromJson.Xml, fromJson.Xsd);
```

## Used by

- [ConduitXP](https://github.com/endpointsystems/conduitxp) — enterprise
  integration framework. Uses this package for XML/JSON/CSV schema inference;
  EDI schema generation stays in ConduitXP because the disassembler depends on
  pipeline infrastructure.
- Phoenixml.Editor (forthcoming) — schema-driven document editor.

## Contributing

Pull requests welcome. By submitting a contribution you agree it is licensed
under Apache-2.0 and that you have the right to do so (DCO: include a
`Signed-off-by:` line in commit messages, or use `git commit --signoff`).

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
