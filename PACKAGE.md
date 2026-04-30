# PhoenixmlDb.Schema.Inference

Open-source XSD inference and JSON/CSV/XML conversion for .NET.

```csharp
using PhoenixmlDb.Schema.Inference;

var result = SchemaGenerator.FromJson(jsonContent, rootElement: "orders");
Console.WriteLine(result.Xsd);
```

Apache-2.0. See https://github.com/phoenixmldb/phoenixml-schema-inference.
