# PhoenixmlDb.Schema.Inference

Open-source XSD inference and JSON/CSV/XML conversion for .NET.

```csharp
using PhoenixmlDb.Schema.Inference.Generation;
using PhoenixmlDb.Schema.Inference.Conversion;

var result = SchemaGenerator.FromJson(jsonContent, rootElement: "orders");
Console.WriteLine(result.Xsd);

// JSON round-trip with type recovery
var roundTripped = XmlToJson.Convert(result.Xml, result.Xsd);
```

Apache-2.0. See https://github.com/phoenixmldb/phoenixml-schema-inference.
