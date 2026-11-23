
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

// ==============================================
// LOAD OFFICIAL BLUEPRINT V1 SCHEMA
// ==============================================

var root = FindRepositoryRoot();

var schemaPath = Path.Combine(
    root, "contracts", "schemas", "blueprint",
    "blueprint.v1.schema.json");

var examplePath = Path.Combine(
    root, "contracts", "examples",
    "blueprint.valid.v1.json");

using var schemaDocument = JsonDocument.Parse(
    File.ReadAllText(schemaPath));

var schema = JsonSchema.Build(
    schemaDocument.RootElement,
    new BuildOptions
    {
        Dialect = Dialect.Draft202012,
        SchemaRegistry = new()
    });

var evaluationOptions = new EvaluationOptions
{
    OutputFormat = OutputFormat.List,
    RequireFormatValidation = true
};

var serializationOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition =
        JsonIgnoreCondition.WhenWritingNull
};

var exampleJson = File.ReadAllText(examplePath);

int passed = 0;
int failed = 0;

// ==============================================
// TEST HELPERS
// ==============================================

void Test(string name, Action action)
{
    try
    {
        action();
        passed++;
        Console.WriteLine("[PASS] " + name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("[FAIL] " + name);
        Console.WriteLine("       " + ex.Message);
    }
}

void Ensure(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

bool IsSchemaValid(JsonNode node)
{
    using var document = JsonDocument.Parse(
        node.ToJsonString());

    return schema.Evaluate(
        document.RootElement,
        evaluationOptions).IsValid;
}

JsonObject Fresh() =>
    JsonNode.Parse(exampleJson)!.AsObject();

void Valid(
    string name,
    Action<JsonObject>? change = null)
{
    Test(name, () =>
    {
        var document = Fresh();
        change?.Invoke(document);

        Ensure(
            IsSchemaValid(document),
            "Expected schema validation to pass.");
    });
}

void Invalid(
    string name,
    Action<JsonObject> change)
{
    Test(name, () =>
    {
        var document = Fresh();
        change(document);

        Ensure(
            !IsSchemaValid(document),
            "Invalid JSON unexpectedly passed schema validation.");
    });
}

// ==============================================
// POSITIVE TESTS
// ==============================================

Test("Schema declares Draft 2020-12", () =>
{
    Ensure(
        schemaDocument.RootElement
            .GetProperty("$schema")
            .GetString() ==
            "https://json-schema.org/draft/2020-12/schema",
        "Incorrect schema dialect.");
});

Valid("Day 01 Blueprint example matches JSON Schema");

Test("Day 01 example passes semantic validation", () =>
{
    var model = JsonSerializer.Deserialize<BlueprintDocument>(
        exampleJson);

    var result = BlueprintContractValidator.Validate(model);

    Ensure(
        result.IsValid,
        string.Join(", ", result.Issues.Select(x => x.Code)));
});

Valid("Suggested requirements without confirmation are valid");

Valid("Valid multiple target platforms", d =>
{
    d["targetPlatforms"]!.AsArray().Add("desktop");
});

Valid("Valid submit action without navigation", d =>
{
    d["actions"]![0]!["kind"] = "submit";

    d["actions"]![0]!.AsObject().Remove(
        "navigationId");

    d["navigation"] = new JsonArray();
});

Test("Typed model serializes with omitted optional nulls", () =>
{
    var model = JsonSerializer.Deserialize<BlueprintDocument>(
        exampleJson)!;

    var json = JsonSerializer.SerializeToNode(
        model, serializationOptions)!;

    Ensure(
        IsSchemaValid(json),
        "Typed Blueprint JSON failed schema validation.");
});

Test("Default null serialization is rejected by schema", () =>
{
    var model = JsonSerializer.Deserialize<BlueprintDocument>(
        exampleJson)!;

    var json = JsonSerializer.SerializeToNode(model)!;

    Ensure(
        !IsSchemaValid(json),
        "Expected explicit null optional fields to be rejected.");
});

// ==============================================
// REAL DAY 03 GENERATOR INTEGRATION
// ==============================================

var requirement = new ReviewedRequirement(
    "REQ-LOGIN",
    "Login",
    "Users must sign in",
    RequirementReviewStatus.Confirmed,
    2,
    [
        new CandidateEvidence(
            "SRC-001",
            "BRD section 2",
            "Users must sign in")
    ],
    Guid.NewGuid(),
    DateTimeOffset.UtcNow
);

var request = new BlueprintGenerationRequest(
    Guid.NewGuid(),
    "BP-GENERATED-001",
    1,
    [requirement],
    [
        new ProposedFeature(
            "FEAT-LOGIN",
            "Login",
            ["REQ-LOGIN"])
    ],
    [
        new ProposedScreen(
            "SCR-LOGIN",
            "Login",
            ["FEAT-LOGIN"]),
        new ProposedScreen(
            "SCR-HOME",
            "Home",
            ["FEAT-LOGIN"])
    ],
    [
        new ProposedComponent(
            "CMP-LOGIN",
            "SCR-LOGIN",
            "Button",
            "filled")
    ],
    [
        new ProposedAction(
            "ACT-LOGIN",
            "CMP-LOGIN",
            "navigate",
            "NAV-LOGIN-HOME")
    ],
    [
        new ProposedNavigation(
            "NAV-LOGIN-HOME",
            "SCR-LOGIN",
            "SCR-HOME",
            "ACT-LOGIN")
    ]
);

var generator = new BlueprintDraftGenerator(
    new Day03StarterComponentRegistry());

var generated = generator.Generate(request);

Invalid("Blueprint ID with a trailing newline is rejected",
    node => node["blueprintId"] = "BP-TRAILING\n");

Test("Generated Blueprint passes full JSON Schema", () =>
{
    var json = JsonSerializer.SerializeToNode(
        generated, serializationOptions)!;

    Ensure(
        IsSchemaValid(json),
        "Generated Blueprint violates v1 JSON Schema.");
});

Test("Generated Blueprint passes semantic validation", () =>
{
    var result = BlueprintContractValidator.Validate(generated);

    Ensure(
        result.IsValid,
        string.Join(", ", result.Issues.Select(x => x.Code)));
});

// ==============================================
// NEGATIVE SCHEMA TESTS
// ==============================================

Invalid("Missing required schemaVersion", d =>
    d.Remove("schemaVersion"));

Invalid("Unsupported schemaVersion", d =>
    d["schemaVersion"] = "2.0.0");

Invalid("Additional root property", d =>
    d["unexpectedProperty"] = "invalid");

Invalid("Wrong projectId JSON type", d =>
    d["projectId"] = 123);

Invalid("Invalid project UUID format", d =>
    d["projectId"] = "not-a-uuid");

Invalid("Revision below minimum", d =>
    d["revision"] = 0);

Invalid("Revision must be integer", d =>
    d["revision"] = 1.5);

Invalid("Approved lifecycle not allowed in v1", d =>
    d["lifecycle"] = "approved");

Invalid("Unsupported platform", d =>
    d["targetPlatforms"]![0] = "iot");

Invalid("Duplicate target platform", d =>
    d["targetPlatforms"]!.AsArray().Add("web"));

Invalid("Wrong Blueprint ID prefix", d =>
    d["blueprintId"] = "PROJECT-001");

Invalid("Invalid component type", d =>
    d["components"]![0]!["type"] = "Script");

Invalid("Component props must be object", d =>
    d["components"]![0]!["props"] = "invalid");

Invalid("Unknown component property", d =>
    d["components"]![0]!["unknownField"] = true);

Invalid("Unknown action kind", d =>
    d["actions"]![0]!["kind"] = "runCode");

Invalid("Navigation ID must use NAV prefix", d =>
    d["navigation"]![0]!["id"] = "ROUTE-001");

Invalid("Duplicate feature reference", d =>
{
    var ids = d["features"]![0]![
        "requirementIds"]!.AsArray();

    ids.Add(ids[0]!.GetValue<string>());
});

Invalid("Confirmed requirement missing reviewer", d =>
    d["requirements"]![0]!.AsObject()
        .Remove("confirmedBy"));

Invalid("Confirmed requirement missing timestamp", d =>
    d["requirements"]![0]!.AsObject()
        .Remove("confirmedAt"));

Invalid("Invalid confirmation date-time", d =>
    d["requirements"]![0]!["confirmedAt"] =
        "not-a-date");

Invalid("Suggested requirement with confirmedBy", d =>
{
    d["requirements"]![1]!["confirmedBy"] = "unknown";
});

Invalid("Blank evidence quote", d =>
{
    d["requirements"]![0]!["evidence"]![0]![
        "quote"] = "";
});

Invalid("Missing source locator", d =>
{
    d["requirements"]![0]!["evidence"]![0]!
        .AsObject().Remove("locator");
});

Invalid("Navigation action without navigationId", d =>
    d["actions"]![0]!.AsObject()
        .Remove("navigationId"));

Invalid("Submit action cannot have navigationId", d =>
    d["actions"]![0]!["kind"] = "submit");

Invalid("Additional requirement property", d =>
{
    d["requirements"]![0]!["approvedByAI"] = true;
});

Invalid("Requirement status cannot be auto-approved", d =>
{
    d["requirements"]![0]!["status"] =
        "auto-approved";
});

Invalid("Missing required features collection", d =>
    d.Remove("features"));

Invalid("Evidence entries must be objects", d =>
{
    d["requirements"]![0]!["evidence"]![0] = "source";
});

// ==============================================
// SEMANTIC VS SCHEMA VALIDATION
// ==============================================

Test("Broken feature reference needs semantic validator", () =>
{
    var json = Fresh();

    json["features"]![0]!["requirementIds"]![0] =
        "REQ-MISSING";

    Ensure(
        IsSchemaValid(json),
        "JSON Schema checks shape, not cross-reference existence.");

    var model = json.Deserialize<BlueprintDocument>();

    var semantic = BlueprintContractValidator.Validate(model);

    Ensure(
        !semantic.IsValid &&
        semantic.Issues.Any(x =>
            x.Code == "BROKEN_REFERENCE"),
        "Semantic validator did not reject broken reference.");
});

Test("Duplicate requirement IDs need semantic validator", () =>
{
    var json = Fresh();

    var duplicated =
        json["requirements"]![0]!.DeepClone();

    json["requirements"]!.AsArray().Add(duplicated);

    Ensure(
        IsSchemaValid(json),
        "Schema does not enforce unique IDs across objects.");

    var model = json.Deserialize<BlueprintDocument>();

    var semantic = BlueprintContractValidator.Validate(model);

    Ensure(
        !semantic.IsValid &&
        semantic.Issues.Any(x =>
            x.Code == "DUPLICATE_ID"),
        "Semantic validator did not detect duplicate IDs.");
});

// ==============================================
// FINAL
// ==============================================

Console.WriteLine();
Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

return failed == 0 ? 0 : 1;

// Locate repository by its checked-in schema.
static string FindRepositoryRoot()
{
    DirectoryInfo? folder =
        new(AppContext.BaseDirectory);

    while (folder is not null)
    {
        var schema = Path.Combine(
            folder.FullName,
            "contracts",
            "schemas",
            "blueprint",
            "blueprint.v1.schema.json");

        if (File.Exists(schema))
            return folder.FullName;

        folder = folder.Parent;
    }

    throw new FileNotFoundException(
        "Cannot find REQVIORA repository's Blueprint v1 schema.");
}
