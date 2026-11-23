
using System.Security.Claims;
using Reqviora.Api.Features.RequirementReview;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;

int passed = 0;
int failed = 0;

var projectId = Guid.NewGuid();
var reviewerId = Guid.NewGuid();

const string requirementId = "REQ-LOGIN-01";

const string acceptJson =
    """{"expectedRevision":1,"decision":"accept"}""";

// ========================================
// TEST HELPERS
// ========================================

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

void Reject<TException>(
    string name, Action action)
    where TException : Exception
{
    Test(name, () =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (ex is TException)
                return;

            throw;
        }

        throw new Exception(
            $"Expected {typeof(TException).Name}.");
    });
}

ClaimsPrincipal SignedIn(Guid id) =>
    new(new ClaimsIdentity(
        [new Claim("sub", id.ToString())],
        "TestVerifiedJwt"));

(FakeReviewStore Store, ReviewApiDecisionBoundary Api)
    Setup(bool authorized = true, bool missing = false)
{
    ReviewedRequirement? requirement = missing
        ? null
        : new ReviewedRequirement(
            requirementId,
            "Login",
            "Users must log in.",
            RequirementReviewStatus.Suggested,
            1,
            [
                new CandidateEvidence(
                    "SRC-001",
                    "BRD page 2",
                    "Login is required.")
            ],
            null,
            null);

    var store = new FakeReviewStore(
        projectId, requirement);

    var policy = new FakeReviewAccessPolicy(
        authorized, projectId, reviewerId);

    var workflow = new RequirementReviewWorkflow(
        store, policy);

    return (
        store,
        new ReviewApiDecisionBoundary(workflow)
    );
}

ReviewApiDecisionReceipt Run(
    ReviewApiDecisionBoundary api,
    string json = acceptJson,
    ClaimsPrincipal? principal = null,
    Guid? requestedProjectId = null) =>
    api.SubmitAsync(
        principal ?? SignedIn(reviewerId),
        requestedProjectId ?? projectId,
        requirementId,
        json
    ).GetAwaiter().GetResult();

// ========================================
// GROUP A: VALID CONTRACT MAPPING
// ========================================

Test("Accept maps to human review command", () =>
{
    var body = ReviewApiDecisionJson.Parse(acceptJson);

    var command = ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId, body);

    Ensure(
        command.Decision == RequirementDecisionKind.Accept,
        "Accept mapping failed.");
});

Test("Edit maps with title and description", () =>
{
    var body = ReviewApiDecisionJson.Parse(
        """
        {
          "expectedRevision": 1,
          "decision": "edit",
          "editedTitle": "Email Login",
          "editedDescription": "Login using email."
        }
        """);

    var command = ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId, body);

    Ensure(
        command.Decision == RequirementDecisionKind.Edit &&
        command.EditedTitle == "Email Login",
        "Edit mapping failed.");
});

Test("Reject maps correctly", () =>
{
    var command = ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        ReviewApiDecisionJson.Parse(
            """{"expectedRevision":1,"decision":"reject"}"""));

    Ensure(
        command.Decision == RequirementDecisionKind.Reject,
        "Reject mapping failed.");
});

Test("Defer maps correctly", () =>
{
    var command = ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        ReviewApiDecisionJson.Parse(
            """{"expectedRevision":1,"decision":"defer"}"""));

    Ensure(
        command.Decision == RequirementDecisionKind.Defer,
        "Defer mapping failed.");
});

Test("Edit accepts empty description", () =>
{
    var command = ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "edit",
            EditedTitle = "Updated",
            EditedDescription = ""
        });

    Ensure(
        command.EditedDescription == "",
        "Empty edit description should be permitted.");
});

// ========================================
// GROUP B: INVALID INPUT REJECTION
// ========================================

Reject<ArgumentException>(
    "Unknown decision rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "auto-approve"
        }));

Reject<ArgumentException>(
    "Unexpected uppercase decision rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "ACCEPT"
        }));

Reject<ArgumentException>(
    "Empty project ID rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        Guid.Empty, requirementId,
        ReviewApiDecisionJson.Parse(acceptJson)));

Reject<ArgumentException>(
    "Missing requirement ID rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, "",
        ReviewApiDecisionJson.Parse(acceptJson)));

Reject<ArgumentException>(
    "Wrong requirement ID prefix rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, "SCREEN-LOGIN",
        ReviewApiDecisionJson.Parse(acceptJson)));

Reject<ArgumentException>(
    "Invalid revision rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 0,
            Decision = "accept"
        }));

Reject<ArgumentException>(
    "Edit without title rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "edit",
            EditedDescription = "Updated login"
        }));

Reject<ArgumentException>(
    "Edit title over 200 characters rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "edit",
            EditedTitle = new string('x', 201),
            EditedDescription = "Description"
        }));

Reject<ArgumentException>(
    "Edit without description rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "edit",
            EditedTitle = "Login"
        }));

Reject<ArgumentException>(
    "Accept with edit fields rejected",
    () => ReviewApiDecisionMapper.ToCommand(
        projectId, requirementId,
        new ReviewApiDecisionBody
        {
            ExpectedRevision = 1,
            Decision = "accept",
            EditedTitle = "Unexpected"
        }));

Reject<ArgumentException>(
    "Client actorId field rejected",
    () => ReviewApiDecisionJson.Parse(
        """
        {
          "expectedRevision": 1,
          "decision": "accept",
          "actorId": "00000000-0000-0000-0000-000000000001"
        }
        """));

Reject<ArgumentException>(
    "Unknown JSON property rejected",
    () => ReviewApiDecisionJson.Parse(
        """
        {
          "expectedRevision": 1,
          "decision": "accept",
          "approvedByAI": true
        }
        """));

Reject<ArgumentException>(
    "Duplicate JSON decision rejected",
    () => ReviewApiDecisionJson.Parse(
        """
        {
          "expectedRevision": 1,
          "decision": "accept",
          "decision": "reject"
        }
        """));

Reject<ArgumentException>(
    "Numeric decision rejected",
    () => ReviewApiDecisionJson.Parse(
        """{"expectedRevision":1,"decision":0}"""));

Reject<ArgumentException>(
    "Malformed JSON rejected",
    () => ReviewApiDecisionJson.Parse("{broken"));

Reject<ArgumentException>(
    "Missing JSON body rejected",
    () => ReviewApiDecisionJson.Parse(null));

Reject<ArgumentException>(
    "Oversized body rejected",
    () => ReviewApiDecisionJson.Parse(
        new string('x', 16_385)));

// ========================================
// GROUP C: AUTHENTICATION BOUNDARY
// ========================================

Reject<UnauthorizedAccessException>(
    "Anonymous user rejected",
    () =>
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", reviewerId.ToString())]));

        ReviewApiPrincipalReader
            .RequireAuthenticatedUser(principal);
    });

Reject<UnauthorizedAccessException>(
    "Missing user claim rejected",
    () =>
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([], "TestVerifiedJwt"));

        ReviewApiPrincipalReader
            .RequireAuthenticatedUser(principal);
    });

Reject<UnauthorizedAccessException>(
    "Non-GUID user claim rejected",
    () =>
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", "not-a-guid")],
                "TestVerifiedJwt"));

        ReviewApiPrincipalReader
            .RequireAuthenticatedUser(principal);
    });

Reject<UnauthorizedAccessException>(
    "Empty GUID user claim rejected",
    () => ReviewApiPrincipalReader
        .RequireAuthenticatedUser(
            SignedIn(Guid.Empty)));

Reject<UnauthorizedAccessException>(
    "Conflicting identity claims rejected",
    () =>
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim("sub", reviewerId.ToString()),
                new Claim(
                    ClaimTypes.NameIdentifier,
                    Guid.NewGuid().ToString())
            ], "TestVerifiedJwt"));

        ReviewApiPrincipalReader
            .RequireAuthenticatedUser(principal);
    });

Reject<UnauthorizedAccessException>(
    "Multiple authenticated identities rejected",
    () =>
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
                [new Claim("sub", reviewerId.ToString())],
                "AuthA"),
            new ClaimsIdentity(
                [new Claim("sub", reviewerId.ToString())],
                "AuthB")
        ]);

        ReviewApiPrincipalReader
            .RequireAuthenticatedUser(principal);
    });

Test("Matching sub and nameidentifier accepted", () =>
{
    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(
        [
            new Claim("sub", reviewerId.ToString()),
            new Claim(
                ClaimTypes.NameIdentifier,
                reviewerId.ToString())
        ], "TestVerifiedJwt"));

    var id = ReviewApiPrincipalReader
        .RequireAuthenticatedUser(principal);

    Ensure(id == reviewerId,
        "Identity mismatch.");
});

// ========================================
// GROUP D: ACCESS & WORKFLOW INTEGRATION
// ========================================

Test("Unauthorized project denied before database read",
    () =>
    {
        var (store, api) = Setup(authorized: false);

        bool denied = false;

        try
        {
            Run(api);
        }
        catch (UnauthorizedAccessException)
        {
            denied = true;
        }

        Ensure(denied, "Unauthorized review was allowed.");
        Ensure(store.ReadCount == 0,
            "Requirement was read before authorization.");
    });

Test("Accept persists human confirmation and audit",
    () =>
    {
        var (store, api) = Setup();

        var receipt = Run(api);

        Ensure(receipt.Status == "confirmed",
            "Wrong response status.");
        Ensure(receipt.Revision == 2,
            "Wrong response revision.");
        Ensure(store.Current!.ConfirmedBy == reviewerId,
            "Wrong confirmation actor.");
        Ensure(store.Events.Count == 1,
            "Missing audit event.");
        Ensure(store.Events[0].ActorId == reviewerId,
            "Wrong audit actor.");
    });

Test("Edit invalidates previous confirmation",
    () =>
    {
        var (store, api) = Setup();

        Run(api); // First, explicitly accept.

        var receipt = Run(
            api,
            """
            {
              "expectedRevision": 2,
              "decision": "edit",
              "editedTitle": "New Login",
              "editedDescription": "Updated flow"
            }
            """);

        Ensure(receipt.Status == "suggested",
            "Edited requirement must be suggested.");
        Ensure(receipt.Revision == 3,
            "Expected revision 3.");
        Ensure(store.Current!.ConfirmedBy is null,
            "Edit must clear confirmation.");
        Ensure(store.Events.Count == 2,
            "Expected both audit events.");
    });

Test("Reject returns rejected status", () =>
{
    var (_, api) = Setup();

    var receipt = Run(
        api,
        """{"expectedRevision":1,"decision":"reject"}""");

    Ensure(receipt.Status == "rejected",
        "Reject response incorrect.");
});

Test("Defer returns deferred status", () =>
{
    var (_, api) = Setup();

    var receipt = Run(
        api,
        """{"expectedRevision":1,"decision":"defer"}""");

    Ensure(receipt.Status == "deferred",
        "Defer response incorrect.");
});

Reject<InvalidOperationException>(
    "Stale requirement revision rejected",
    () =>
    {
        var (_, api) = Setup();

        Run(api,
            """{"expectedRevision":5,"decision":"accept"}""");
    });

Test("Concurrent update rejected without audit",
    () =>
    {
        var (store, api) = Setup();

        store.ForceConflict = true;
        bool rejected = false;

        try
        {
            Run(api);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Ensure(rejected, "Concurrency conflict not detected.");
        Ensure(store.Current!.Revision == 1,
            "Revision changed during conflict.");
        Ensure(store.Events.Count == 0,
            "Audit event saved despite conflict.");
    });

Reject<KeyNotFoundException>(
    "Missing requirement rejected",
    () =>
    {
        var (_, api) = Setup(missing: true);
        Run(api);
    });

Test("Other project cannot access this requirement",
    () =>
    {
        var (store, api) = Setup();
        bool denied = false;

        try
        {
            Run(
                api,
                requestedProjectId: Guid.NewGuid());
        }
        catch (UnauthorizedAccessException)
        {
            denied = true;
        }

        Ensure(denied, "Cross-project access allowed.");
        Ensure(store.ReadCount == 0,
            "Cross-project data was read.");
    });

// ========================================
// FINAL RESULTS
// ========================================

Console.WriteLine();
Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

return failed == 0 ? 0 : 1;

// ========================================
// TEST-ONLY FAKE PERSISTENCE
// ========================================

sealed class FakeReviewStore(
    Guid projectId,
    ReviewedRequirement? initial)
    : IRequirementReviewStore
{
    public ReviewedRequirement? Current { get; private set; } =
        initial;

    public List<RequirementReviewEvent> Events { get; } = [];

    public int ReadCount { get; private set; }

    public bool ForceConflict { get; set; }

    public Task<RequirementReviewSnapshot?> FindAsync(
        Guid requestedProjectId,
        string requestedRequirementId,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;

        if (requestedProjectId != projectId ||
            Current is null ||
            Current.Id != requestedRequirementId)
        {
            return Task.FromResult<RequirementReviewSnapshot?>(
                null);
        }

        return Task.FromResult<RequirementReviewSnapshot?>(
            new RequirementReviewSnapshot(
                projectId, Current));
    }

    public Task<bool> TryCommitAsync(
        Guid requestedProjectId,
        int expectedRevision,
        RequirementReviewResult result,
        CancellationToken cancellationToken = default)
    {
        if (ForceConflict ||
            requestedProjectId != projectId ||
            Current is null ||
            Current.Revision != expectedRevision ||
            result.Requirement.Revision != expectedRevision + 1 ||
            result.Requirement.Id != Current.Id)
        {
            return Task.FromResult(false);
        }

        Current = result.Requirement;
        Events.Add(result.Event);

        return Task.FromResult(true);
    }
}

sealed class FakeReviewAccessPolicy(
    bool allowed,
    Guid expectedProjectId,
    Guid expectedReviewerId)
    : IRequirementReviewAccessPolicy
{
    public Task<bool> CanReviewAsync(
        Guid projectId,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            allowed &&
            projectId == expectedProjectId &&
            authenticatedUserId == expectedReviewerId);
    }
}
