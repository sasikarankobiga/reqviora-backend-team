
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;

int passed = 0;
int failed = 0;

var projectId = Guid.NewGuid();
var reviewerId = Guid.NewGuid();

var candidate = new RequirementCandidate(
    "REQ-LOGIN-01",
    "Login",
    "Users must sign in.",
    "en",
    0.95m,
    [
        new CandidateEvidence(
            "SRC-001", "BRD section 2", "Users must sign in.")
    ]
);

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
        Console.WriteLine(ex.Message);
    }
}

void Ensure(bool condition)
{
    if (!condition)
        throw new Exception("Assertion failed.");
}

void Reject<T>(string name, Action action)
    where T : Exception
{
    Test(name, () =>
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception(
            $"Expected {typeof(T).Name}.");
    });
}

(FakeReviewStore Store, RequirementReviewWorkflow Workflow)
    Setup(bool allowed = true, bool missing = false)
{
    var requirement = missing
        ? null
        : RequirementReviewService.CreateCandidate(candidate);

    var store = new FakeReviewStore(
        projectId, requirement);

    var workflow = new RequirementReviewWorkflow(
        store, new FakeReviewAccessPolicy(allowed));

    return (store, workflow);
}

SubmitRequirementReview Command(
    RequirementDecisionKind decision,
    int revision = 1,
    string? title = null,
    string? description = null,
    Guid? targetProjectId = null) =>
    new(
        targetProjectId ?? projectId,
        candidate.Id,
        revision,
        decision,
        title,
        description
    );

RequirementReviewResult Run(
    RequirementReviewWorkflow workflow,
    SubmitRequirementReview command,
    Guid? actor = null) =>
    workflow.SubmitAsync(
        command, actor ?? reviewerId)
        .GetAwaiter().GetResult();

// 1
Test("Candidate starts as suggested", () =>
{
    var (store, _) = Setup();
    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Suggested);
});

// 2
Test("Accept persists confirmed requirement", () =>
{
    var (store, workflow) = Setup();
    Run(workflow, Command(
        RequirementDecisionKind.Accept));

    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Confirmed);
    Ensure(store.Current.Revision == 2);
});

// 3
Test("Accept saves reviewer audit event", () =>
{
    var (store, workflow) = Setup();
    Run(workflow, Command(
        RequirementDecisionKind.Accept));

    Ensure(store.Events.Count == 1);
    Ensure(store.Events[0].ActorId == reviewerId);
    Ensure(store.Events[0].PreviousRevision == 1);
    Ensure(store.Events[0].NewRevision == 2);
});

// 4
Test("Edit resets requirement to suggested", () =>
{
    var (store, workflow) = Setup();

    Run(workflow, Command(
        RequirementDecisionKind.Edit,
        title: "Updated Login",
        description: "Users sign in using email."));

    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Suggested);
    Ensure(store.Current.Title == "Updated Login");
    Ensure(store.Current.ConfirmedBy is null);
});

// 5
Test("Edited requirement needs explicit acceptance", () =>
{
    var (store, workflow) = Setup();

    Run(workflow, Command(
        RequirementDecisionKind.Edit,
        title: "Updated Login",
        description: "Email login"));

    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Suggested);

    Run(workflow, Command(
        RequirementDecisionKind.Accept,
        revision: 2));

    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Confirmed);
    Ensure(store.Current.Revision == 3);
    Ensure(store.Events.Count == 2);
});

// 6
Test("Reject persists rejected status", () =>
{
    var (store, workflow) = Setup();

    Run(workflow, Command(
        RequirementDecisionKind.Reject));

    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Rejected);
});

// 7
Test("Defer persists deferred status", () =>
{
    var (store, workflow) = Setup();

    Run(workflow, Command(
        RequirementDecisionKind.Defer));

    Ensure(
        store.Current!.Status ==
        RequirementReviewStatus.Deferred);
});

// 8
Reject<UnauthorizedAccessException>(
    "Unauthorized reviewer rejected before reading",
    () =>
    {
        var (store, workflow) = Setup(allowed: false);

        try
        {
            Run(workflow, Command(
                RequirementDecisionKind.Accept));
        }
        finally
        {
            Ensure(store.ReadCount == 0);
        }
    });

// 9
Reject<UnauthorizedAccessException>(
    "Missing authenticated user rejected",
    () =>
    {
        var (_, workflow) = Setup();

        workflow.SubmitAsync(
            Command(RequirementDecisionKind.Accept),
            Guid.Empty).GetAwaiter().GetResult();
    });

// 10
Reject<InvalidOperationException>(
    "Stale review revision rejected",
    () =>
    {
        var (_, workflow) = Setup();

        Run(workflow, Command(
            RequirementDecisionKind.Accept,
            revision: 5));
    });

// 11
Reject<KeyNotFoundException>(
    "Missing requirement rejected",
    () =>
    {
        var (_, workflow) = Setup(missing: true);

        Run(workflow, Command(
            RequirementDecisionKind.Accept));
    });

// 12
Test("Concurrent update fails without saving", () =>
{
    var (store, workflow) = Setup();
    store.ForceConflict = true;

    try
    {
        Run(workflow, Command(
            RequirementDecisionKind.Accept));
        throw new Exception("Expected concurrency conflict.");
    }
    catch (InvalidOperationException)
    {
        Ensure(store.Current!.Revision == 1);
        Ensure(store.Events.Count == 0);
    }
});

// 13
Reject<InvalidOperationException>(
    "Rejected requirement cannot be accepted",
    () =>
    {
        var (_, workflow) = Setup();

        Run(workflow, Command(
            RequirementDecisionKind.Reject));

        Run(workflow, Command(
            RequirementDecisionKind.Accept,
            revision: 2));
    });

// 14
Test("Invalid edit does not save or audit", () =>
{
    var (store, workflow) = Setup();

    try
    {
        Run(workflow, Command(
            RequirementDecisionKind.Edit,
            title: "",
            description: "Updated description"));

        throw new Exception("Invalid edit accepted.");
    }
    catch (ArgumentException)
    {
        Ensure(store.Current!.Revision == 1);
        Ensure(store.Events.Count == 0);
    }
});

// 15
Reject<KeyNotFoundException>(
    "Requirement cannot be reviewed via other project",
    () =>
    {
        var (_, workflow) = Setup();

        Run(workflow, Command(
            RequirementDecisionKind.Accept,
            targetProjectId: Guid.NewGuid()));
    });

Console.WriteLine();
Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

return failed == 0 ? 0 : 1;

// Test-only repository; not production persistence.
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
        string requirementId,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;

        if (requestedProjectId != projectId ||
            Current is null ||
            Current.Id != requirementId)
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
            result.Requirement.Id != Current.Id ||
            result.Event.RequirementId != Current.Id)
        {
            return Task.FromResult(false);
        }

        Current = result.Requirement;
        Events.Add(result.Event);

        return Task.FromResult(true);
    }
}

sealed class FakeReviewAccessPolicy(bool allowed)
    : IRequirementReviewAccessPolicy
{
    public Task<bool> CanReviewAsync(
        Guid projectId,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(allowed);
    }
}
