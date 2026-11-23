
using System.Text.Json;
using System.Text.Json.Nodes;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

const string ValidJson = """
{
  "schemaVersion": "1.0.0",
  "blueprintId": "BP-DEMO-01",
  "projectId": "9e421d26-04d4-4fb2-9dbc-7ad36c639201",
  "revision": 1,
  "lifecycle": "draft",
  "targetPlatforms": ["web"],
  "requirements": [
    {
      "id": "REQ-LOGIN",
      "text": "Users can sign in",
      "origin": "ai",
      "status": "confirmed",
      "confirmedBy": "reviewer-01",
      "confirmedAt": "2026-11-18T09:00:00Z",
      "evidence": [
        {
          "sourceId": "SRC-01",
          "locator": "BRD section 2",
          "quote": "sign in"
        }
      ]
    }
  ],
  "features": [
    {
      "id": "FEAT-LOGIN",
      "name": "Login",
      "requirementIds": ["REQ-LOGIN"]
    }
  ],
  "screens": [
    {
      "id": "SCR-LOGIN",
      "name": "Login",
      "featureIds": ["FEAT-LOGIN"],
      "componentIds": ["CMP-LOGIN-BTN"]
    },
    {
      "id": "SCR-HOME",
      "name": "Home",
      "featureIds": ["FEAT-LOGIN"],
      "componentIds": []
    }
  ],
  "components": [
    {
      "id": "CMP-LOGIN-BTN",
      "screenId": "SCR-LOGIN",
      "type": "Button",
      "props": {
        "variant": "filled"
      },
      "actionIds": ["ACT-LOGIN"],
      "childComponentIds": []
    }
  ],
  "actions": [
    {
      "id": "ACT-LOGIN",
      "componentId": "CMP-LOGIN-BTN",
      "kind": "navigate",
      "navigationId": "NAV-LOGIN-HOME"
    }
  ],
  "navigation": [
    {
      "id": "NAV-LOGIN-HOME",
      "sourceScreenId": "SCR-LOGIN",
      "targetScreenId": "SCR-HOME",
      "actionId": "ACT-LOGIN"
    }
  ]
}
""";

int passed = 0;
int failed = 0;

// GENERAL TEST RUNNER

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
        Console.WriteLine(
            "[FAIL] " + name + ": " + ex.Message);
    }
}

BlueprintDocument Read(JsonObject? node = null) =>
    node is null
        ? JsonSerializer.Deserialize<BlueprintDocument>(
            ValidJson)!
        : node.Deserialize<BlueprintDocument>()!;

void Good(
    string name,
    Action<JsonObject>? change = null) =>
    Test(name, () =>
    {
        var node = JsonNode.Parse(ValidJson)!.AsObject();

        change?.Invoke(node);

        var result =
            BlueprintContractValidator.Validate(Read(node));

        if (!result.IsValid)
        {
            throw new Exception(
                string.Join(
                    "; ",
                    result.Issues.Select(
                        x => x.Code + " " + x.Path)));
        }
    });

void Bad(
    string name,
    string expectedCode,
    Action<JsonObject> change) =>
    Test(name, () =>
    {
        var node = JsonNode.Parse(ValidJson)!.AsObject();

        change(node);

        var result =
            BlueprintContractValidator.Validate(Read(node));

        if (!result.Issues.Any(
            x => x.Code == expectedCode))
        {
            throw new Exception(
                "Missing " + expectedCode + "; actual: " +
                string.Join(
                    ", ",
                    result.Issues.Select(x => x.Code)));
        }
    });

// VALID BLUEPRINT TESTS

Good("Valid Blueprint fixture");

Test("Existing Day 01 example passes semantic validation",
    () =>
    {
        var folder =
            new DirectoryInfo(AppContext.BaseDirectory);

        string? file = null;

        while (folder is not null)
        {
            var candidate = Path.Combine(
                folder.FullName,
                "contracts",
                "examples",
                "blueprint.valid.v1.json");

            if (File.Exists(candidate))
            {
                file = candidate;
                break;
            }

            folder = folder.Parent;
        }

        if (file is null)
            throw new Exception(
                "Day 01 Blueprint example not found.");

        var document =
            JsonSerializer.Deserialize<BlueprintDocument>(
                File.ReadAllText(file));

        var result =
            BlueprintContractValidator.Validate(document);

        if (!result.IsValid)
            throw new Exception(
                string.Join(
                    ", ",
                    result.Issues.Select(x => x.Code)));
    });

Test("Validator does not modify Blueprint", () =>
{
    var doc = Read();
    var before = JsonSerializer.Serialize(doc);

    BlueprintContractValidator.Validate(doc);

    if (JsonSerializer.Serialize(doc) != before)
        throw new Exception(
            "Validator mutated Blueprint data.");
});

Test("Null Blueprint rejected", () =>
{
    var result = BlueprintContractValidator.Validate(null);

    if (!result.Issues.Any(
        x => x.Code == "NULL_BLUEPRINT"))
    {
        throw new Exception("Null Blueprint accepted.");
    }
});

Test("Day 03 generator output passes semantic validation",
    () =>
    {
        var id = Guid.NewGuid();

        var requirement = new ReviewedRequirement(
            "REQ-LOGIN",
            "Login",
            "User must sign in",
            RequirementReviewStatus.Confirmed,
            2,
            [
                new CandidateEvidence(
                    "SRC-01",
                    "BRD section 2",
                    "sign in")
            ],
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        var request = new BlueprintGenerationRequest(
            id,
            "BP-GENERATED-01",
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
                    "CMP-LOGIN-BTN",
                    "SCR-LOGIN",
                    "Button",
                    "filled")
            ],

            [
                new ProposedAction(
                    "ACT-LOGIN",
                    "CMP-LOGIN-BTN",
                    "navigate",
                    "NAV-LOGIN-HOME")
            ],

            [
                new ProposedNavigation(
                    "NAV-LOGIN-HOME",
                    "SCR-LOGIN",
                    "SCR-HOME",
                    "ACT-LOGIN")
            ]);

        var generator = new BlueprintDraftGenerator(
            new Day03StarterComponentRegistry());

        var draft = generator.Generate(request);

        var result =
            BlueprintContractValidator.Validate(draft);

        if (!result.IsValid)
        {
            throw new Exception(
                string.Join(
                    ", ",
                    result.Issues.Select(x => x.Code)));
        }
    });

// BLUEPRINT IDENTITY TESTS

Bad(
    "Unsupported schema version",
    "SCHEMA_VERSION",
    d => d["schemaVersion"] = "2.0.0");

Bad(
    "Missing Blueprint ID",
    "INVALID_ID",
    d => d["blueprintId"] = "");

Bad(
    "Wrong Blueprint ID prefix",
    "INVALID_ID",
    d => d["blueprintId"] = "PROJECT-01");

Bad(
    "Empty project GUID",
    "PROJECT_ID",
    d => d["projectId"] = Guid.Empty.ToString());

Bad(
    "Invalid revision",
    "REVISION",
    d => d["revision"] = 0);

Bad(
    "Invalid lifecycle",
    "LIFECYCLE",
    d => d["lifecycle"] = "approved");

Bad(
    "Empty target platform list",
    "TARGET_PLATFORM",
    d => d["targetPlatforms"] = new JsonArray());

Bad(
    "Unsupported target platform",
    "TARGET_PLATFORM",
    d => d["targetPlatforms"]![0] = "iot");

Bad(
    "Duplicate target platform",
    "DUPLICATE_REFERENCE",
    d => d["targetPlatforms"]!.AsArray().Add("web"));

Bad(
    "Missing requirements collection",
    "MISSING_COLLECTION",
    d => d["requirements"] = null);

// REQUIREMENT TESTS

Bad(
    "Duplicate requirement ID",
    "DUPLICATE_ID",
    d => d["requirements"]!.AsArray().Add(
        d["requirements"]![0]!.DeepClone()));

Bad(
    "Invalid requirement prefix",
    "INVALID_ID",
    d => d["requirements"]![0]!["id"] = "R-LOGIN");

Bad(
    "Blank requirement text",
    "REQUIREMENT_TEXT",
    d => d["requirements"]![0]!["text"] = "  ");

Bad(
    "Invalid requirement origin",
    "REQUIREMENT_ORIGIN",
    d => d["requirements"]![0]!["origin"] =
        "user-confirmed");

Bad(
    "Invalid requirement status",
    "REQUIREMENT_STATUS",
    d => d["requirements"]![0]!["status"] =
        "auto-approved");

Bad(
    "Missing human confirmation",
    "MISSING_CONFIRMATION",
    d => d["requirements"]![0]!["confirmedBy"] = null);

Bad(
    "Evidence missing",
    "MISSING_EVIDENCE",
    d => d["requirements"]![0]!["evidence"] =
        new JsonArray());

Bad(
    "Evidence source missing",
    "INVALID_EVIDENCE",
    d => d["requirements"]![0]!["evidence"]![0]![
        "sourceId"] = "");

Bad(
    "Unconfirmed record has approval data",
    "UNEXPECTED_CONFIRMATION",
    d => d["requirements"]![0]!["status"] =
        "suggested");

// FEATURE AND SCREEN TESTS

Bad(
    "Duplicate feature ID",
    "DUPLICATE_ID",
    d => d["features"]!.AsArray().Add(
        d["features"]![0]!.DeepClone()));

Bad(
    "Feature references missing requirement",
    "BROKEN_REFERENCE",
    d => d["features"]![0]!["requirementIds"]![0] =
        "REQ-NOTFOUND");

Bad(
    "Feature references unconfirmed requirement",
    "UNCONFIRMED_REQUIREMENT",
    d =>
    {
        d["requirements"]![0]!["status"] = "suggested";
        d["requirements"]![0]!["confirmedBy"] = null;
        d["requirements"]![0]!["confirmedAt"] = null;
    });

Bad(
    "Duplicate feature-to-requirement reference",
    "DUPLICATE_REFERENCE",
    d => d["features"]![0]!["requirementIds"]!
        .AsArray().Add("REQ-LOGIN"));

Bad(
    "Screen references missing feature",
    "BROKEN_REFERENCE",
    d => d["screens"]![0]!["featureIds"]![0] =
        "FEAT-MISSING");

Bad(
    "Duplicate screen ID",
    "DUPLICATE_ID",
    d => d["screens"]!.AsArray().Add(
        d["screens"]![0]!.DeepClone()));

Bad(
    "Screen links missing component",
    "BROKEN_REFERENCE",
    d => d["screens"]![0]!["componentIds"]![0] =
        "CMP-MISSING");

Bad(
    "Component absent from owning screen",
    "COMPONENT_NOT_LISTED",
    d => d["screens"]![0]!["componentIds"] =
        new JsonArray());

// COMPONENT TESTS

Bad(
    "Unsupported component type",
    "UNSUPPORTED_COMPONENT",
    d => d["components"]![0]!["type"] = "Script");

Bad(
    "Component belongs to another screen",
    "SCREEN_COMPONENT_MISMATCH",
    d => d["components"]![0]!["screenId"] = "SCR-HOME");

Bad(
    "Component props null",
    "COMPONENT_PROPS",
    d => d["components"]![0]!["props"] = null);

Bad(
    "Missing child component",
    "BROKEN_REFERENCE",
    d => d["components"]![0]!["childComponentIds"]!
        .AsArray().Add("CMP-MISSING"));

Bad(
    "Self-referencing component",
    "COMPONENT_CYCLE",
    d => d["components"]![0]!["childComponentIds"]!
        .AsArray().Add("CMP-LOGIN-BTN"));

Bad(
    "Two-component child cycle",
    "COMPONENT_CYCLE",
    d =>
    {
        d["components"]!.AsArray().Add(
            JsonNode.Parse(
                """
                {
                  "id": "CMP-SECOND",
                  "screenId": "SCR-LOGIN",
                  "type": "Card",
                  "props": {},
                  "actionIds": [],
                  "childComponentIds": ["CMP-LOGIN-BTN"]
                }
                """));

        d["components"]![0]!["childComponentIds"]!
            .AsArray().Add("CMP-SECOND");

        d["screens"]![0]!["componentIds"]!
            .AsArray().Add("CMP-SECOND");
    });

// ACTION AND NAVIGATION TESTS

Bad(
    "Duplicate action ID",
    "DUPLICATE_ID",
    d => d["actions"]!.AsArray().Add(d["actions"]![0]!.DeepClone()));

Bad(
    "Second component cannot claim another component's action",
    "COMPONENT_ACTION_MISMATCH",
    d =>
    {
        var duplicate = d["components"]![0]!.DeepClone();
        duplicate["id"] = "CMP-UNRELATED";
        var screenId = duplicate["screenId"]!.GetValue<string>();
        var screen = d["screens"]!.AsArray()
            .Single(x => x!["id"]!.GetValue<string>() == screenId)!;
        screen["componentIds"]!.AsArray().Add("CMP-UNRELATED");
        d["components"]!.AsArray().Add(duplicate);
    });

Bad(
    "Two actions cannot claim the same route",
    "NAVIGATION_ACTION_MISMATCH",
    d =>
    {
        var duplicate = d["actions"]![0]!.DeepClone();
        duplicate["id"] = "ACT-UNRELATED";
        var componentId = duplicate["componentId"]!.GetValue<string>();
        var component = d["components"]!.AsArray()
            .Single(x => x!["id"]!.GetValue<string>() == componentId)!;
        component["actionIds"]!.AsArray().Add("ACT-UNRELATED");
        d["actions"]!.AsArray().Add(duplicate);
    });

Bad(
    "Action references unknown component",
    "BROKEN_REFERENCE",
    d => d["actions"]![0]!["componentId"] =
        "CMP-MISSING");

Bad(
    "Action missing from component",
    "ACTION_NOT_LISTED",
    d => d["components"]![0]!["actionIds"] =
        new JsonArray());

Bad(
    "Unsupported action",
    "ACTION_KIND",
    d => d["actions"]![0]!["kind"] =
        "runJavaScript");

Bad(
    "Navigate action missing route",
    "BROKEN_REFERENCE",
    d => d["actions"]![0]!["navigationId"] =
        "NAV-MISSING");

Bad(
    "Non-navigate action cannot point to route",
    "INVALID_ACTION_ROUTE",
    d => d["actions"]![0]!["kind"] = "submit");

Bad(
    "Route target missing",
    "BROKEN_REFERENCE",
    d => d["navigation"]![0]!["targetScreenId"] =
        "SCR-MISSING");

Bad(
    "Duplicate navigation ID",
    "DUPLICATE_ID",
    d => d["navigation"]!.AsArray().Add(
        d["navigation"]![0]!.DeepClone()));

Bad(
    "Wrong navigation ID prefix",
    "INVALID_ID",
    d => d["navigation"]![0]!["id"] =
        "ROUTE-LOGIN-HOME");

Bad(
    "Route action mismatch",
    "NAVIGATION_ACTION_MISMATCH",
    d => d["navigation"]![0]!["id"] =
        "NAV-OTHER");

Bad(
    "Route source differs from action component",
    "NAVIGATION_SOURCE_MISMATCH",
    d => d["navigation"]![0]!["sourceScreenId"] =
        "SCR-HOME");

// FINAL TEST RESULT

Console.WriteLine();

Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

return failed == 0 ? 0 : 1;
