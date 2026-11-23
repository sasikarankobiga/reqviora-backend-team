
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.UIGeneration;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

int passed = 0;
int failed = 0;

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

void Reject(string name, Action action)
{
    Test(name, () =>
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new Exception(
            "Expected operation to be rejected.");
    });
}

void Ensure(
    bool condition,
    string message = "Assertion failed")
{
    if (!condition)
        throw new Exception(message);
}

var projectId = Guid.NewGuid();
var requestId = Guid.NewGuid();
var reviewerId = Guid.NewGuid();

var source = new SourceEvidenceRef(
    "SRC-001",
    "BRD page 2",
    2,
    0,
    16,
    "User must login"
);

var candidate = new RequirementCandidate(
    "REQ-LOGIN-01",
    "Login requirement",
    "Users should access a login screen.",
    "en",
    0.92m,
    [
        new CandidateEvidence(
            source.SourceId,
            source.Locator,
            source.Quote)
    ]
);

var extractionPayload =
    new RequirementExtractionPayload(
        "1.0",
        [candidate]
    );

var options = new JsonSerializerOptions(
    JsonSerializerDefaults.Web);

string SerializePayload(
    RequirementExtractionPayload payload) =>
    JsonSerializer.Serialize(payload, options);

var aiRequest = new AiProviderRequest(
    projectId,
    requestId,
    AiOperation.RequirementExtraction,
    "en",
    "Extract login requirements."
);

AiProviderResponse Response(string json) => new(
    requestId,
    AiRunStatus.Completed,
    json,
    null
);

RequirementExtractionPayload Extract(
    RequirementExtractionPayload payload) =>
    RequirementExtractionValidator.Parse(
        aiRequest,
        Response(SerializePayload(payload)),
        [source]
    );

var screenRequest = aiRequest with { Operation = AiOperation.ScreenSuggestion };
var blueprintJsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var suggested =
    RequirementReviewService.CreateCandidate(candidate);

RequirementReviewDecision Decision(
    RequirementDecisionKind kind,
    int expectedRevision = 1,
    string? title = null,
    string? description = null) =>
    new(
        suggested.Id,
        expectedRevision,
        kind,
        reviewerId,
        DateTimeOffset.UtcNow,
        title,
        description
    );

var accepted = RequirementReviewService.Apply(
    suggested,
    Decision(RequirementDecisionKind.Accept)
).Requirement;

var generator = new BlueprintDraftGenerator(
    new Day03StarterComponentRegistry()
);

// FIXED: Both action and navigation use NAV-LOGIN-HOME.
BlueprintGenerationRequest Plan(
    int revision = 1,
    string variant = "filled") =>
    new(
        projectId,
        "BP-KOBIGA-001",
        revision,

        [accepted],

        [
            new ProposedFeature(
                "FEAT-LOGIN",
                "Login",
                ["REQ-LOGIN-01"])
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
                variant)
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
        ]
    );

var current = generator.Generate(Plan());

var regeneration = new SafeRegenerationService(
    new Day03StarterComponentRegistry()
);

SafeRegenerationRequest Policy(
    int revision = 1,
    IReadOnlyCollection<string>? features = null,
    IReadOnlyCollection<string>? screens = null,
    IReadOnlyCollection<string>? components = null) =>
    new(
        revision,
        features ?? Array.Empty<string>(),
        screens ?? Array.Empty<string>(),
        components ?? Array.Empty<string>()
    );

// =====================================================
// TASK 8: AI REQUIREMENT EXTRACTION
// =====================================================

Test("Valid AI candidates", () =>
{
    var payload = Extract(extractionPayload);

    Ensure(payload.Candidates.Count == 1);
});

Test("Candidate remains unconfirmed", () =>
{
    Ensure(
        suggested.Status ==
        RequirementReviewStatus.Suggested);
});

Test("Valid Tamil candidate", () =>
{
    var tamil = candidate with
    {
        LanguageCode = "ta-LK"
    };

    Ensure(
        Extract(new("1.0", [tamil]))
            .Candidates.Count == 1);
});

Test("Empty extraction candidates allowed", () =>
{
    Ensure(
        Extract(new("1.0", []))
            .Candidates.Count == 0);
});

Reject("Duplicate AI candidate rejected", () =>
{
    Extract(new("1.0", [candidate, candidate]));
});

Reject("Unknown source evidence rejected", () =>
{
    Extract(new(
        "1.0",
        [
            candidate with
            {
                Evidence =
                [
                    new CandidateEvidence(
                        "SRC-UNKNOWN",
                        "page 9",
                        "Unknown")
                ]
            }
        ]
    ));
});

Reject("Missing candidate evidence rejected", () =>
{
    Extract(new(
        "1.0",
        [
            candidate with
            {
                Evidence = []
            }
        ]
    ));
});

Reject("Out of range confidence rejected", () =>
{
    Extract(new(
        "1.0",
        [
            candidate with
            {
                Confidence = 1.4m
            }
        ]
    ));
});

Reject("Unsupported extraction contract rejected", () =>
{
    Extract(new("2.0", [candidate]));
});

Reject("AI candidate missing required confidence is rejected", () =>
{
    var json = JsonNode.Parse(SerializePayload(extractionPayload))!.AsObject();
    json["candidates"]![0]!.AsObject().Remove("confidence");
    RequirementExtractionValidator.Parse(aiRequest, Response(json.ToJsonString()), [source]);
});

Reject("Duplicate JSON field in structured extraction is rejected", () =>
{
    var json = SerializePayload(extractionPayload).Insert(1, "\"contractVersion\":\"1.0\",");
    RequirementExtractionValidator.Parse(aiRequest, Response(json), [source]);
});

Reject("Wrong AI request ID rejected", () =>
{
    RequirementExtractionValidator.Parse(
        aiRequest,
        new AiProviderResponse(
            Guid.NewGuid(),
            AiRunStatus.Completed,
            SerializePayload(extractionPayload),
            null
        ),
        [source]
    );
});

Reject("Non-extraction AI operation rejected", () =>
{
    RequirementExtractionValidator.Parse(
        aiRequest with
        {
            Operation = AiOperation.ScreenSuggestion
        },
        Response(SerializePayload(extractionPayload)),
        [source]
    );
});

Reject("AI cannot insert approved status", () =>
{
    string json = SerializePayload(extractionPayload)
        .Replace(
            "\"confidence\":",
            "\"status\":\"confirmed\",\"confidence\":"
        );

    RequirementExtractionValidator.Parse(
        aiRequest,
        Response(json),
        [source]
    );
});

// =====================================================
// TASK 9: REQUIREMENT REVIEW & CLARIFICATION
// =====================================================

Test("Explicit accept confirms requirement", () =>
{
    Ensure(
        accepted.Status ==
        RequirementReviewStatus.Confirmed
        &&
        accepted.ConfirmedBy == reviewerId);
});

Test("Edit returns requirement to suggested", () =>
{
    var edited = RequirementReviewService.Apply(
        accepted,
        Decision(
            RequirementDecisionKind.Edit,
            2,
            "Updated login",
            "User needs an updated login screen."
        )
    ).Requirement;

    Ensure(
        edited.Status ==
        RequirementReviewStatus.Suggested
        &&
        edited.ConfirmedBy is null
        &&
        edited.Revision == 3);
});

Test("Reject makes requirement rejected", () =>
{
    var result = RequirementReviewService.Apply(
        suggested,
        Decision(RequirementDecisionKind.Reject)
    );

    Ensure(
        result.Requirement.Status ==
        RequirementReviewStatus.Rejected);
});

Test("Defer makes requirement deferred", () =>
{
    var result = RequirementReviewService.Apply(
        suggested,
        Decision(RequirementDecisionKind.Defer)
    );

    Ensure(
        result.Requirement.Status ==
        RequirementReviewStatus.Deferred);
});

Test("Clarification answer does not approve", () =>
{
    var question = new ClarificationQuestion(
        Guid.NewGuid(),
        suggested.Id,
        "Should login use email?",
        null,
        1,
        false
    );

    var answered = ClarificationService.Answer(
        question,
        new ClarificationAnswerRequest(
            question.Id,
            1,
            "Yes, email."
        )
    );

    Ensure(answered.IsResolved);

    Ensure(
        suggested.Status ==
        RequirementReviewStatus.Suggested);
});

Reject("Stale review revision rejected", () =>
{
    RequirementReviewService.Apply(
        accepted,
        Decision(
            RequirementDecisionKind.Edit,
            1,
            "Changed",
            "Description"
        )
    );
});

Reject("Confirmed requirement cannot auto-reconfirm", () =>
{
    RequirementReviewService.Apply(
        accepted,
        Decision(
            RequirementDecisionKind.Accept,
            2
        )
    );
});

Reject("Anonymous reviewer rejected", () =>
{
    RequirementReviewService.Apply(
        suggested,
        Decision(RequirementDecisionKind.Accept)
            with
        {
            ActorId = Guid.Empty
        }
    );
});

Reject("Invalid empty edit rejected", () =>
{
    RequirementReviewService.Apply(
        suggested,
        Decision(
            RequirementDecisionKind.Edit,
            1,
            "",
            "Description"
        )
    );
});

// =====================================================
// TASK 10: BLUEPRINT GENERATION
// =====================================================

Test("Confirmed requirements generate draft", () =>
{
    var blueprint = generator.Generate(Plan());

    Ensure(blueprint.Lifecycle == "draft");

    Ensure(blueprint.Requirements.Count == 1);

    Ensure(
        blueprint.Requirements[0].Status ==
        "confirmed");

    Ensure(
        blueprint.Features[0].RequirementIds[0] ==
        "REQ-LOGIN-01");
});

Test("Navigation and component traceability", () =>
{
    Ensure(
        current.Navigation[0].TargetScreenId ==
        "SCR-HOME");

    Ensure(
        current.Components[0].ActionIds[0] ==
        "ACT-LOGIN");

    Ensure(
        current.Requirements[0].Evidence[0].SourceId ==
        "SRC-001");
});

Reject("Unconfirmed Blueprint requirement rejected", () =>
{
    generator.Generate(
        Plan() with
        {
            Requirements = [suggested]
        }
    );
});

Reject("Unsupported Blueprint component rejected", () =>
{
    generator.Generate(
        Plan(1, "unknown-variant")
    );
});

Reject("Broken feature requirement reference rejected", () =>
{
    generator.Generate(
        Plan() with
        {
            Features =
            [
                new ProposedFeature(
                    "FEAT-LOGIN",
                    "Login",
                    ["REQ-MISSING"])
            ]
        }
    );
});

Reject("Broken navigation reference rejected", () =>
{
    generator.Generate(
        Plan() with
        {
            Navigation =
            [
                new ProposedNavigation(
                    "NAV-LOGIN-HOME",
                    "SCR-LOGIN",
                    "SCR-MISSING",
                    "ACT-LOGIN")
            ]
        }
    );
});

Reject("Duplicate screen ID rejected", () =>
{
    generator.Generate(
        Plan() with
        {
            Screens =
            [
                new ProposedScreen(
                    "SCR-LOGIN",
                    "Login",
                    ["FEAT-LOGIN"]),

                new ProposedScreen(
                    "SCR-LOGIN",
                    "Duplicate",
                    ["FEAT-LOGIN"])
            ]
        }
    );
});

// =====================================================
// TASK 11: SAFE REGENERATION
// =====================================================

Test("Valid new draft revision accepted", () =>
{
    var newer = generator.Generate(Plan(2));

    var result = regeneration.TryApply(
        current,
        newer,
        Policy()
    );

    Ensure(result.Applied);

    Ensure(result.Blueprint.Revision == 2);

    Ensure(current.Revision == 1);
});

Test("Stale regeneration rejected", () =>
{
    var newer = generator.Generate(Plan(2));

    var result = regeneration.TryApply(
        current,
        newer,
        Policy(99)
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason == "REVISION_CONFLICT");

    Ensure(
        ReferenceEquals(
            result.Blueprint,
            current));
});

Test("Protected component edits preserved", () =>
{
    var changed = generator.Generate(
        Plan(2, "outlined")
    );

    var result = regeneration.TryApply(
        current,
        changed,
        Policy(
            1,
            components: ["CMP-LOGIN-BTN"]
        )
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason ==
        "PROTECTED_COMPONENT_CHANGED");
});

Test("Protected screen changes rejected", () =>
{
    var request = Plan(2) with
    {
        Screens =
        [
            new ProposedScreen(
                "SCR-LOGIN",
                "Modified Login",
                ["FEAT-LOGIN"]),

            new ProposedScreen(
                "SCR-HOME",
                "Home",
                ["FEAT-LOGIN"])
        ]
    };

    var changed = generator.Generate(request);

    var result = regeneration.TryApply(
        current,
        changed,
        Policy(
            1,
            screens: ["SCR-LOGIN"]
        )
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason ==
        "PROTECTED_SCREEN_CHANGED");
});

Test("Confirmed requirement cannot be rewritten", () =>
{
    var newer = generator.Generate(Plan(2));

    var original = newer.Requirements[0];

    newer.Requirements[0] = new BlueprintRequirement
    {
        Id = original.Id,
        Text = "AI changed a confirmed requirement",
        Status = original.Status,
        Origin = original.Origin,
        ConfirmedBy = original.ConfirmedBy,
        ConfirmedAt = original.ConfirmedAt,
        Evidence = original.Evidence
    };

    var result = regeneration.TryApply(
        current,
        newer,
        Policy()
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason ==
        "CONFIRMATION_CHANGED");
});

Test("Broken regenerated navigation rejected", () =>
{
    var newer = generator.Generate(Plan(2));

    var route = newer.Navigation[0];

    newer.Navigation[0] = new BlueprintNavigation
    {
        Id = route.Id,
        SourceScreenId = route.SourceScreenId,
        TargetScreenId = "SCR-MISSING",
        ActionId = route.ActionId
    };

    var result = regeneration.TryApply(
        current,
        newer,
        Policy()
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason ==
        "INVALID_GENERATED_GRAPH");
});

Test("Duplicate confirmed IDs fall back without altering the current draft", () =>
{
    var newer = generator.Generate(Plan(2));
    newer.Requirements.Add(newer.Requirements[0]);

    var result = regeneration.TryApply(current, newer, Policy());

    Ensure(!result.Applied && result.Reason == "INVALID_GENERATED_GRAPH");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Changing a protected component's navigation falls back", () =>
{
    var plan = Plan(2);
    var redirect = plan.Navigation[0] with { TargetScreenId = "SCR-LOGIN" };
    var newer = generator.Generate(plan with { Navigation = [redirect] });

    var result = regeneration.TryApply(current, newer, Policy(components: ["CMP-LOGIN-BTN"]));

    Ensure(!result.Applied && result.Reason == "PROTECTED_COMPONENT_CHANGED");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Provider Blueprint preserves the source-to-screen trace", () =>
{
    var newer = generator.Generate(Plan(2));
    var result = regeneration.TryApplyAiOutput(
        current, Policy(), screenRequest, Response(JsonSerializer.Serialize(newer, blueprintJsonOptions)));

    Ensure(result.Applied);
    var blueprint = result.Blueprint;
    var component = blueprint.Components.Single();
    var screen = blueprint.Screens.Single(item => item.Id == component.ScreenId);
    var feature = blueprint.Features.Single(item => item.Id == screen.FeatureIds.Single());
    var requirement = blueprint.Requirements.Single(item => item.Id == feature.RequirementIds.Single());

    Ensure(requirement.ConfirmedBy == reviewerId.ToString());
    Ensure(requirement.Evidence.Single().Quote == source.Quote);
    Ensure(requirement.Evidence.Single().Locator == source.Locator);
    Ensure(current.Revision == 1 && blueprint.Revision == 2);
});

Test("Provider Blueprint cannot omit a required collection", () =>
{
    var proposal = JsonSerializer.SerializeToNode(generator.Generate(Plan(2)), blueprintJsonOptions)!.AsObject();
    proposal.Remove("navigation");
    var result = regeneration.TryApplyAiOutput(current, Policy(), screenRequest, Response(proposal.ToJsonString()));

    Ensure(!result.Applied && result.Reason == "INVALID_AI_OUTPUT");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Schema-valid empty design does not replace the current design", () =>
{
    var proposal = JsonSerializer.SerializeToNode(generator.Generate(Plan(2)), blueprintJsonOptions)!.AsObject();
    foreach (var section in new[] { "features", "screens", "components", "actions", "navigation" })
        proposal[section] = new JsonArray();

    var result = regeneration.TryApplyAiOutput(current, Policy(), screenRequest, Response(proposal.ToJsonString()));

    Ensure(!result.Applied && result.Reason == "INVALID_GENERATED_GRAPH");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Schema-valid provider output cannot add a human confirmation", () =>
{
    var proposal = JsonSerializer.SerializeToNode(generator.Generate(Plan(2)), blueprintJsonOptions)!.AsObject();
    var requirements = proposal["requirements"]!.AsArray();
    var invented = requirements[0]!.DeepClone();
    invented["id"] = "REQ-NEW";
    requirements.Add(invented);

    var result = regeneration.TryApplyAiOutput(current, Policy(), screenRequest, Response(proposal.ToJsonString()));

    Ensure(!result.Applied && result.Reason == "CONFIRMATION_CHANGED");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Provider Blueprint with duplicate keys falls back", () =>
{
    var proposal = JsonSerializer.Serialize(generator.Generate(Plan(2)), blueprintJsonOptions)
        .Insert(1, "\"revision\":2,");
    var result = regeneration.TryApplyAiOutput(current, Policy(), screenRequest, Response(proposal));

    Ensure(!result.Applied && result.Reason == "INVALID_AI_OUTPUT");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Provider failure and unavailability return the prior draft", () =>
{
    foreach (var status in new[] { AiRunStatus.Failed, AiRunStatus.Unavailable })
    {
        var response = new AiProviderResponse(requestId, status, null, "PROVIDER_ERROR");
        var result = regeneration.TryApplyAiOutput(current, Policy(), screenRequest, response);

        Ensure(!result.Applied && ReferenceEquals(result.Blueprint, current));
        Ensure(result.Reason == (status == AiRunStatus.Failed ? "AI_FAILED" : "AI_UNAVAILABLE"));
    }
});

Test("Provider response must match the screen suggestion request", () =>
{
    var proposed = JsonSerializer.Serialize(generator.Generate(Plan(2)), blueprintJsonOptions);
    var response = Response(proposed) with { RequestId = Guid.NewGuid() };
    var result = regeneration.TryApplyAiOutput(current, Policy(), screenRequest, response);

    Ensure(!result.Applied && result.Reason == "AI_RESPONSE_MISMATCH");
    Ensure(ReferenceEquals(result.Blueprint, current));
});

Test("Approved revision remains immutable", () =>
{
    var approved = new BlueprintDocument
    {
        SchemaVersion = current.SchemaVersion,
        BlueprintId = current.BlueprintId,
        ProjectId = current.ProjectId,
        Revision = current.Revision,
        Lifecycle = "approved",
        TargetPlatforms = current.TargetPlatforms,
        Requirements = current.Requirements,
        Features = current.Features,
        Screens = current.Screens,
        Components = current.Components,
        Actions = current.Actions,
        Navigation = current.Navigation
    };

    var result = regeneration.TryApply(
        approved,
        generator.Generate(Plan(2)),
        Policy()
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason ==
        "IMMUTABLE_REVISION");
});

Test("Wrong Blueprint identity rejected", () =>
{
    var other = generator.Generate(
        Plan(2) with
        {
            BlueprintId = "BP-OTHER"
        }
    );

    var result = regeneration.TryApply(
        current,
        other,
        Policy()
    );

    Ensure(!result.Applied);

    Ensure(
        result.Reason ==
        "BLUEPRINT_IDENTITY_MISMATCH");
});

// =====================================================
// BLUEPRINT V1 CONTRACT COMPATIBILITY TESTS
// =====================================================

Test("Blueprint navigation uses NAV prefix", () =>
{
    Ensure(
        current.Navigation[0].Id.StartsWith(
            "NAV-",
            StringComparison.Ordinal)
    );

    Ensure(
        current.Actions[0].NavigationId ==
        current.Navigation[0].Id);
});

Test("Blueprint component type matches schema", () =>
{
    Ensure(
        current.Components[0].Type == "Button");
});

Test("Blueprint requirement origin matches schema", () =>
{
    Ensure(
        current.Requirements[0].Origin == "ai");
});

Test("Serialized Blueprint has expected contract values", () =>
{
    string json = JsonSerializer.Serialize(current);

    using var document = JsonDocument.Parse(json);

    var root = document.RootElement;

    Ensure(
        root.GetProperty("schemaVersion")
            .GetString() == "1.0.0");

    Ensure(
        root.GetProperty("components")[0]
            .GetProperty("type")
            .GetString() == "Button");

    Ensure(
        root.GetProperty("navigation")[0]
            .GetProperty("id")
            .GetString() == "NAV-LOGIN-HOME");

    Ensure(
        root.GetProperty("requirements")[0]
            .GetProperty("origin")
            .GetString() == "ai");

    Ensure(
        root.GetProperty("actions")[0]
            .GetProperty("kind")
            .GetString() == "navigate");
});

// =====================================================
// FINAL TEST RESULT
// =====================================================

Console.WriteLine();

Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

return failed == 0 ? 0 : 1;
