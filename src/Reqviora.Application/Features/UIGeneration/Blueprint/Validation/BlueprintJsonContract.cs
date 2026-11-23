using System.Text.Json;
using Json.Schema;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

public static class BlueprintJsonContract
{
    private static readonly JsonSchema Schema = LoadSchema();

    public static BlueprintDocument Parse(string json)
    {
        if (json.Length > AiContractValidator.MaxStructuredJsonLength)
            throw new ArgumentException("Blueprint output exceeds the supported size.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = 32
            });

            var result = Schema.Evaluate(document.RootElement, new EvaluationOptions
            {
                RequireFormatValidation = true
            });

            if (!result.IsValid)
                throw new ArgumentException("Blueprint output does not match the v1 schema.");

            return document.RootElement.Deserialize<BlueprintDocument>()
                ?? throw new ArgumentException("Blueprint output is required.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Invalid Blueprint JSON.", exception);
        }
    }

    private static JsonSchema LoadSchema()
    {
        using var resource = typeof(BlueprintJsonContract).Assembly
            .GetManifestResourceStream("Reqviora.Blueprint.V1.Schema.json")
            ?? throw new InvalidOperationException("Embedded Blueprint v1 schema is missing.");
        using var document = JsonDocument.Parse(resource);

        // Schema evaluation retains elements from the schema document.
        return JsonSchema.Build(document.RootElement.Clone(), new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new()
        });
    }
}
