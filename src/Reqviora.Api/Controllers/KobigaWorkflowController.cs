using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.WorkflowIntegration;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore.Storage;

namespace Reqviora.Api.Controllers;

/// <summary>
/// Completes Kobiga tasks 9-11 without replacing the existing protected review API.
/// Review decisions: POST /api/projects/{projectId}/requirements/{requirementId}/review.
/// Review data lives in the existing reqviora_review schema; Blueprint revisions remain
/// in immutable DesignDraftVersions. Generate again after an explicit review decision.
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/workflow-intelligence")]
public sealed class KobigaWorkflowController(
    ReqvioraDbContext db,
    IRequirementReviewStore reviewStore,
    IBlueprintDraftGenerator generator,
    SafeRegenerationService regeneration,
    ProjectDraftService drafts,
    IStructuredAiProvider ai,
    IOptions<AiOptions> aiOptions) : ControllerBase
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string ClarificationsProperty = "kobigaClarifications";

    public sealed record CreateClarificationBody(
        Guid? ExpectedConcurrencyToken,
        string RequirementId,
        string Question);

    public sealed record AnswerClarificationBody(
        Guid ExpectedConcurrencyToken,
        int ExpectedRevision,
        string Answer);

    public sealed record GenerateBlueprintBody(
        Guid? ExpectedConcurrencyToken,
        string BlueprintId,
        IReadOnlyList<string> ConfirmedRequirementIds,
        IReadOnlyList<ProposedFeature> Features,
        IReadOnlyList<ProposedScreen> Screens,
        IReadOnlyList<ProposedComponent> Components,
        IReadOnlyList<ProposedAction> Actions,
        IReadOnlyList<ProposedNavigation> Navigation);

    public sealed record RegeneratePreviewBody(
        Guid ExpectedConcurrencyToken,
        SafeRegenerationRequest Policy,
        BlueprintDocument ProposedBlueprint);

    public sealed record RegenerateBody(
        Guid ExpectedConcurrencyToken,
        SafeRegenerationRequest Policy,
        string LanguageCode,
        string Instructions,
        bool ConsentGranted);

    public sealed record BlueprintWorkflowResponse(
        bool Applied,
        string Reason,
        Guid ConcurrencyToken,
        int DraftVersion,
        BlueprintDocument Blueprint);

    // Task 9: Persist an unconfirmed candidate in the existing review store.
    // An authenticated owner can stage a reviewed/extracted candidate; this is not approval.
    // The caller must preserve the original evidence refs from the extraction response.
    [HttpPost("candidates")]
    [ProducesResponseType<ReviewedRequirement>(StatusCodes.Status201Created)]
    public async Task<IActionResult> StageCandidate(
        Guid projectId, RequirementCandidate candidate, CancellationToken ct)
    {
        await ProjectApiAccess.RequireOwnerAsync(db, projectId, ActorId, ct);
        var proposed = KobigaWorkflowPolicy.StageCandidate(candidate);
        try
        {

    var staged = await InsertCandidateAsync(
    projectId, ActorId, proposed, ct);

     if (!staged)
    {
    return ConflictLocked();
    }

        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(new { error = "REQUIREMENT_ALREADY_STAGED" });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { error = "REQUIREMENT_ALREADY_STAGED" });
        }
        catch (DbException ex) when (
    db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" &&
    ex.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
    ex.Message.Contains(
        "UNIQUE constraint failed:",
        StringComparison.OrdinalIgnoreCase))
{
    return Conflict(new { error = "REQUIREMENT_ALREADY_STAGED" });
}

        return CreatedAtAction(nameof(GetReview),
            new { projectId, requirementId = proposed.Id }, proposed);
    }

    // After accept/edit/reject/defer, use this to retrieve the authoritative review state.
    [HttpGet("reviews/{requirementId}")]
    public async Task<IActionResult> GetReview(
        Guid projectId, string requirementId, CancellationToken ct)
    {
        await ProjectApiAccess.RequireOwnerAsync(db, projectId, ActorId, ct);
        var result = await reviewStore.FindAsync(projectId, requirementId, ct);
        return result is null ? NotFound() : Ok(result.Requirement);
    }

    // Task 9: Clarification is persisted in the versioned draft JSON and never auto-approves.
    [HttpGet("clarifications")]
    public async Task<IActionResult> GetClarifications(Guid projectId, CancellationToken ct)
    {
        var draft = await drafts.ResumeAsync(projectId, ActorId, ct);
        return Ok(ReadClarifications(draft?.DraftJson ?? "{}"));
    }

    [HttpPost("clarifications")]
    public async Task<IActionResult> CreateClarification(
        Guid projectId, CreateClarificationBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.RequirementId) ||
            string.IsNullOrWhiteSpace(body.Question) || body.Question.Length > 1500)
            return BadRequest(new { error = "INVALID_CLARIFICATION" });

        var (draft, blueprint) = await LoadStateAsync(projectId, ct);
        if (!TokenMatches(draft, body.ExpectedConcurrencyToken)) return ConflictToken();
        if (draft?.ApprovedRevisionId is not null) return ConflictLocked();
        if (await reviewStore.FindAsync(projectId, body.RequirementId, ct) is null)
            return NotFound(new { error = "REQUIREMENT_NOT_STAGED" });

        var list = ReadClarifications(draft?.DraftJson ?? "{}");
        var item = new ClarificationQuestion(
            Guid.NewGuid(), body.RequirementId, body.Question.Trim(), null, 1, false);
        list.Add(item);
        var saved = await SaveClarificationsAsync(projectId, draft, blueprint, list, ct);
        return Ok(new { clarification = item, saved.ConcurrencyToken, saved.Version });
    }

    [HttpPost("clarifications/{questionId:guid}/answer")]
    public async Task<IActionResult> AnswerClarification(
        Guid projectId, Guid questionId, AnswerClarificationBody body, CancellationToken ct)
    {
        var (draft, blueprint) = await LoadStateAsync(projectId, ct);
        if (draft is null || !TokenMatches(draft, body.ExpectedConcurrencyToken))
            return ConflictToken();
        if (draft.ApprovedRevisionId is not null) return ConflictLocked();

        var list = ReadClarifications(draft.DraftJson);
        var index = list.FindIndex(item => item.Id == questionId);
        if (index < 0) return NotFound();
        list[index] = ClarificationService.Answer(list[index],
            new ClarificationAnswerRequest(questionId, body.ExpectedRevision, body.Answer));
        var saved = await SaveClarificationsAsync(projectId, draft, blueprint, list, ct);
        return Ok(new { clarification = list[index], saved.ConcurrencyToken, saved.Version });
    }

    // Task 10: Read confirmation from the SERVER review store, never from request-supplied claims.
    // The request supplies only chosen IDs and graph proposals, not confirmation metadata.
    [HttpPost("blueprints/generate")]
    public async Task<IActionResult> GenerateBlueprint(
        Guid projectId, GenerateBlueprintBody body, CancellationToken ct)
    {
        var (draft, current) = await LoadStateAsync(projectId, ct);
        if (!TokenMatches(draft, body.ExpectedConcurrencyToken)) return ConflictToken();
        if (draft?.ApprovedRevisionId is not null) return ConflictLocked();
        if (body.ConfirmedRequirementIds is null || body.ConfirmedRequirementIds.Count is < 1 or > 200 ||
            body.ConfirmedRequirementIds.Distinct(StringComparer.Ordinal).Count() != body.ConfirmedRequirementIds.Count)
            return BadRequest(new { error = "INVALID_REQUIREMENT_SELECTION" });

        var confirmed = new List<ReviewedRequirement>();
        foreach (var id in body.ConfirmedRequirementIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { error = "INVALID_REQUIREMENT_ID" });
            var snapshot = await reviewStore.FindAsync(projectId, id, ct);
            if (snapshot?.Requirement.Status != RequirementReviewStatus.Confirmed)
                return Conflict(new { error = "REQUIREMENT_NOT_CONFIRMED", requirementId = id });
            confirmed.Add(snapshot.Requirement);
        }
        KobigaWorkflowPolicy.ValidateConfirmedSelection(body.ConfirmedRequirementIds, confirmed);

        // Do not silently drop an earlier confirmed requirement still confirmed in review store.
        if (current is not null)
        {
            var selected = body.ConfirmedRequirementIds.ToHashSet(StringComparer.Ordinal);
            foreach (var previous in current.Requirements.Where(r => r.Status == "confirmed"))
            {
                if (selected.Contains(previous.Id)) continue;
                var latest = await reviewStore.FindAsync(projectId, previous.Id, ct);
                if (latest?.Requirement.Status == RequirementReviewStatus.Confirmed)
                    return Conflict(new { error = "CONFIRMED_REQUIREMENT_OMITTED", requirementId = previous.Id });
            }
        }

        var revision = current is null ? 1 : checked(current.Revision + 1);
        var blueprintId = current?.BlueprintId ?? body.BlueprintId;
        if (current is not null && body.BlueprintId != current.BlueprintId)
            return Conflict(new { error = "BLUEPRINT_ID_MISMATCH" });

        var request = new BlueprintGenerationRequest(
            projectId, blueprintId, revision, confirmed, body.Features,
            body.Screens, body.Components, body.Actions, body.Navigation);
        var proposed = generator.Generate(request); // Also checks component registry and graph references.
        var saved = await drafts.SaveReviewedDesignAsync(
            projectId, ActorId, draft?.DraftJson ?? "{}", proposed,
            body.ExpectedConcurrencyToken, ct);
        return Ok(new BlueprintWorkflowResponse(
            true, "GENERATED_FROM_CONFIRMED_REVIEWS", saved.ConcurrencyToken, saved.Version, proposed));
    }

    // Task 11: deterministic, read-only fallback preview for a generated proposal.
    [HttpPost("blueprints/regenerate-preview")]
    public async Task<IActionResult> RegeneratePreview(
        Guid projectId, RegeneratePreviewBody body, CancellationToken ct)
    {
        var (draft, current) = await LoadStateAsync(projectId, ct);
        if (draft is null || !TokenMatches(draft, body.ExpectedConcurrencyToken)) return ConflictToken();
        if (draft.ApprovedRevisionId is not null) return ConflictLocked();
        if (current is null) return NotFound(new { error = "BLUEPRINT_NOT_FOUND" });
        if (body.Policy is null || body.ProposedBlueprint is null)
            return BadRequest(new { error = "INVALID_REGENERATION_REQUEST" });
        var result = regeneration.TryApply(current, body.ProposedBlueprint, body.Policy);
        result = ProtectAllRequirements(current, result);
        return Ok(new BlueprintWorkflowResponse(
            result.Applied, result.Reason, draft.ConcurrencyToken, draft.Version, result.Blueprint));
    }

    // Task 11: call the server-configured provider only; never trust a client-supplied AI response.
    [HttpPost("blueprints/regenerate")]
    public async Task<IActionResult> Regenerate(
        Guid projectId, RegenerateBody body, CancellationToken ct)
    {
        var (draft, current) = await LoadStateAsync(projectId, ct);
        if (draft is null || !TokenMatches(draft, body.ExpectedConcurrencyToken)) return ConflictToken();
        if (draft.ApprovedRevisionId is not null) return ConflictLocked();
        if (current is null) return NotFound(new { error = "BLUEPRINT_NOT_FOUND" });
        if (body.Policy is null ||
            !InputStudioContractValidator.IsSupportedLanguage(body.LanguageCode) ||
            string.IsNullOrWhiteSpace(body.Instructions))
            return BadRequest(new { error = "INVALID_REGENERATION_REQUEST" });
        if (!body.ConsentGranted)
            return BadRequest(new { error = "AI_CONSENT_REQUIRED" });
        if (!aiOptions.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI provider approval is pending.");

        var prompt = $"Current Blueprint JSON (do not modify requirements):\n{JsonSerializer.Serialize(current, WebJson)}\n" +
            $"Design request: {body.Instructions}\n" +
            "Return a complete Blueprint v1 JSON document. Increase revision by exactly one. " +
            "Preserve all requirements and every human-confirmed value without modification.";
        if (prompt.Length > 100_000)
            return BadRequest(new { error = "REGENERATION_INPUT_TOO_LARGE" });

        var request = new AiProviderRequest(projectId, Guid.NewGuid(),
            AiOperation.ScreenSuggestion, body.LanguageCode, prompt);
        var response = await ai.ExecuteAsync(request, ct);
        var result = regeneration.TryApplyAiOutput(current, body.Policy, request, response);
        result = ProtectAllRequirements(current, result);
        if (!result.Applied)
            return Ok(new BlueprintWorkflowResponse(false, result.Reason,
                draft.ConcurrencyToken, draft.Version, result.Blueprint));

        // The service rechecks ownership, approval lock and concurrency token before saving.
        var saved = await drafts.SaveDesignAsync(
            projectId, ActorId, draft.DraftJson, result.Blueprint,
            body.ExpectedConcurrencyToken, ct);
        return Ok(new BlueprintWorkflowResponse(true, result.Reason,
            saved.ConcurrencyToken, saved.Version, result.Blueprint));
    }

    private Guid ActorId => ProjectApiAccess.RequireUserId(User);

    private async Task<(ProjectDraft? Draft, BlueprintDocument? Blueprint)> LoadStateAsync(
        Guid projectId, CancellationToken ct)
    {
        var draft = await drafts.ResumeAsync(projectId, ActorId, ct);
        if (draft is null) return (null, null);
        var version = await db.DesignDraftVersions.AsNoTracking().SingleAsync(
            x => x.ProjectDraftId == draft.Id && x.Version == draft.Version, ct);
        return (draft, ProjectApiAccess.ReadBlueprint(version.BlueprintJson));
    }

    private static bool TokenMatches(ProjectDraft? draft, Guid? token) =>
        draft is null ? token is null : token == draft.ConcurrencyToken;

    private static SafeRegenerationResult ProtectAllRequirements(
        BlueprintDocument current, SafeRegenerationResult result) =>
        !result.Applied || KobigaWorkflowPolicy.RequirementsUnchanged(current, result.Blueprint)
            ? result
            : new SafeRegenerationResult(false, "REQUIREMENTS_CHANGED", current);

    private static IActionResult ConflictToken() =>
        new ConflictObjectResult(new { error = "DRAFT_CONCURRENCY_CONFLICT" });

    private static IActionResult ConflictLocked() =>
        new ConflictObjectResult(new { error = "APPROVED_DRAFT_IS_IMMUTABLE" });

    private static List<ClarificationQuestion> ReadClarifications(string draftJson)
    {
        var document = JsonNode.Parse(draftJson)?.AsObject()
            ?? throw new InvalidDataException("Stored project draft is not an object.");
        if (document[ClarificationsProperty] is null) return [];
        return document[ClarificationsProperty]!.Deserialize<List<ClarificationQuestion>>(WebJson)
            ?? throw new InvalidDataException("Stored clarifications are invalid.");
    }

    private async Task<ProjectDraft> SaveClarificationsAsync(
        Guid projectId, ProjectDraft? draft, BlueprintDocument? blueprint,
        List<ClarificationQuestion> list, CancellationToken ct)
    {
        var root = JsonNode.Parse(draft?.DraftJson ?? "{}")!.AsObject();
        root[ClarificationsProperty] = JsonSerializer.SerializeToNode(list, WebJson);
        var json = root.ToJsonString(WebJson);
        return blueprint is null
            ? await drafts.SaveAsync(projectId, ActorId, json, draft?.ConcurrencyToken, ct)
            : await drafts.SaveDesignAsync(projectId, ActorId, json, blueprint,
                draft?.ConcurrencyToken, ct);
    }



private async Task<bool> InsertCandidateAsync(
        Guid projectId,
        Guid actorId,
        ReviewedRequirement requirement,
        CancellationToken ct)
    {
        // EF and raw SQL share one database transaction.
        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var connection = db.Database.GetDbConnection();

        // Lock the project before inserting the candidate.
        // Approval and review decisions must follow the same lock order.
        var provider = db.Database.ProviderName;

        string? projectLockSql = provider switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => """
                SELECT "Id"
                FROM "Projects"
                WHERE "Id" = @project_id
                FOR UPDATE;
                """,

            "Microsoft.EntityFrameworkCore.SqlServer" => """
                SELECT [Id]
                FROM [Projects] WITH (UPDLOCK, HOLDLOCK)
                WHERE [Id] = @project_id;
                """,

            // SQLite is used only by this integration-test setup.
            // It cannot validate production row-lock behavior.
            "Microsoft.EntityFrameworkCore.Sqlite" => null,

            _ => throw new NotSupportedException(
                $"Unsupported database provider: {provider}")
        };

        if (projectLockSql is not null)
        {
            await using var projectLock = connection.CreateCommand();

            projectLock.Transaction = transaction.GetDbTransaction();
            projectLock.CommandText = projectLockSql;

            AddParameter(projectLock, "@project_id", projectId);

            var lockedProject =
                await projectLock.ExecuteScalarAsync(ct);

            if (lockedProject is null)
            {
                throw new UnauthorizedAccessException(
                    "Project access is required.");
            }
        }

        // Security: Do not stage new requirements while
        // the current project draft is already approved.
        // This runs inside the existing transaction,
        // after acquiring the project lock.
        var isDraftApproved = await db.ProjectDrafts
            .AsNoTracking()
            .AnyAsync(
                draft => draft.ProjectId == projectId &&
                         draft.ApprovedRevisionId != null,
                ct);

        if (isDraftApproved)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }



        // Step 1: Save candidate as Suggested.
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();

            var jsonExpr = db.Database.IsSqlServer() ||
                db.Database.ProviderName ==
                    "Microsoft.EntityFrameworkCore.Sqlite"
                ? "@evidence_json"
                : "CAST(@evidence_json AS jsonb)";

            command.CommandText = $"""
                INSERT INTO reqviora_review.requirements
                    (project_id, requirement_id, title,
                     description, status, revision,
                     evidence_json, confirmed_by, confirmed_at)
                VALUES
                    (@project_id, @requirement_id, @title,
                     @description, 'Suggested', 1,
                     {jsonExpr}, NULL, NULL)
                """;

            AddParameter(command, "@project_id", projectId);
            AddParameter(command, "@requirement_id", requirement.Id);
            AddParameter(command, "@title", requirement.Title);
            AddParameter(command, "@description", requirement.Description);

            AddParameter(
                command,
                "@evidence_json",
                JsonSerializer.Serialize(requirement.Evidence));

            await command.ExecuteNonQueryAsync(ct);
        }

        // Step 2: Use EF Core for project ownership and marker.
        var project = await db.Projects.SingleOrDefaultAsync(
            x => x.Id == projectId && x.OwnerId == actorId,
            ct);

        if (project is null)
        {
            throw new UnauthorizedAccessException(
                "Project owner access is required.");
        }

        project.HasRequirementReviewHistory = true;

        // Save marker inside the same transaction.
        await db.SaveChangesAsync(ct);

        // Both operations succeed together.
        await transaction.CommitAsync(ct);
        return true;
    }




    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}