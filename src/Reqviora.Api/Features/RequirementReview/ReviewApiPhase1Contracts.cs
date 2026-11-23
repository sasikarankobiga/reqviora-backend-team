
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Api.Features.RequirementReview;

internal sealed class ReviewApiValidationException(string message, Exception? innerException = null)
    : ArgumentException(message, innerException);

internal sealed class ReviewApiAuthenticationException(string message)
    : UnauthorizedAccessException(message);

// ===========================================
// 1. INPUT CONTRACT
// ===========================================

// No actorId, reviewerId or approvedBy field.
// Reviewer's identity MUST come from the server's
// verified authentication context.

[JsonUnmappedMemberHandling(
    JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewApiDecisionBody
{
    [JsonPropertyName("expectedRevision")]
    public int ExpectedRevision { get; init; }

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("editedTitle")]
    public string? EditedTitle { get; init; }

    [JsonPropertyName("editedDescription")]
    public string? EditedDescription { get; init; }
}

// ===========================================
// 2. RESPONSE CONTRACT
// ===========================================

public sealed record ReviewApiDecisionReceipt(
    string RequirementId,
    string Status,
    int Revision
);

// ===========================================
// 3. STRICT JSON INPUT PARSER
// ===========================================

public static class ReviewApiDecisionJson
{
    internal const int MaxBodyBytes = 16_384;

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false
        };

    public static ReviewApiDecisionBody Parse(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ReviewApiValidationException(
                "Review request body is required.");
        }

        if (Encoding.UTF8.GetByteCount(json) >
            MaxBodyBytes)
        {
            throw new ReviewApiValidationException(
                "Review request body is too large.");
        }

        try
        {
            return JsonSerializer.Deserialize
                <ReviewApiDecisionBody>(
                    json, Options)
                ?? throw new ReviewApiValidationException(
                    "Review request must be an object.");
        }
        catch (JsonException ex)
        {
            throw new ReviewApiValidationException(
                "Invalid review JSON contract.", ex);
        }
    }
}

// ===========================================
// 4. REQUEST-TO-COMMAND MAPPING
// ===========================================

public static class ReviewApiDecisionMapper
{
    private static readonly Regex RequirementIdPattern =
        new(
            @"\AREQ-[A-Z0-9][A-Z0-9._-]*\z",
            RegexOptions.CultureInvariant);

    public static SubmitRequirementReview ToCommand(
        Guid projectId,
        string? requirementId,
        ReviewApiDecisionBody? body)
    {
        if (body is null)
        {
            throw new ReviewApiValidationException(
                "Review request is required.");
        }

        if (projectId == Guid.Empty)
        {
            throw new ReviewApiValidationException(
                "Valid project ID is required.");
        }

        if (string.IsNullOrWhiteSpace(requirementId) ||
            requirementId.Length > 100 ||
            !RequirementIdPattern.IsMatch(requirementId))
        {
            throw new ReviewApiValidationException(
                "Invalid requirement ID.");
        }

        if (body.ExpectedRevision < 1)
        {
            throw new ReviewApiValidationException(
                "Expected revision must be positive.");
        }

        // Explicitly parse human review actions.
        // Do not accept arbitrary enum integers.
        RequirementDecisionKind kind =
            body.Decision switch
            {
                "accept" => RequirementDecisionKind.Accept,
                "edit" => RequirementDecisionKind.Edit,
                "reject" => RequirementDecisionKind.Reject,
                "defer" => RequirementDecisionKind.Defer,
                _ => throw new ReviewApiValidationException(
                    "Unsupported review decision.")
            };

        if (kind == RequirementDecisionKind.Edit)
        {
            if (string.IsNullOrWhiteSpace(
                    body.EditedTitle) ||
                body.EditedTitle.Length > 200 ||
                body.EditedDescription is null ||
                body.EditedDescription.Length > 4000)
            {
                throw new ReviewApiValidationException(
                    "Edit requires a valid title and description.");
            }
        }
        else
        {
            // Prevent edit-related values from being
            // silently accepted for other decisions.
            if (body.EditedTitle is not null ||
                body.EditedDescription is not null)
            {
                throw new ReviewApiValidationException(
                    "Edit fields are only valid for edit decisions.");
            }
        }

        return new SubmitRequirementReview(
            projectId,
            requirementId,
            body.ExpectedRevision,
            kind,
            body.EditedTitle,
            body.EditedDescription
        );
    }
}

// ===========================================
// 5. AUTHENTICATED IDENTITY BOUNDARY
// ===========================================

public static class ReviewApiPrincipalReader
{
    public static Guid RequireAuthenticatedUser(
        ClaimsPrincipal? principal)
    {
        // IMPORTANT:
        // IsAuthenticated alone does NOT verify a JWT.
        // Production callers must supply a principal
        // established by trusted ASP.NET authentication
        // middleware after token validation.

        var authenticatedIdentities =
            principal?.Identities
                .Where(identity =>
                    identity.IsAuthenticated)
                .ToArray()
            ?? [];

        // Fail closed for missing or ambiguous identities.
        if (authenticatedIdentities.Length != 1)
        {
            throw new ReviewApiAuthenticationException(
                "A verified user identity is required.");
        }

        var identity = authenticatedIdentities[0];

        var subjectClaims = identity.Claims
            .Where(claim =>
                claim.Type == "sub" ||
                claim.Type == ClaimTypes.NameIdentifier)
            .ToArray();

        if (subjectClaims.Length == 0)
        {
            throw new ReviewApiAuthenticationException(
                "User ID claim is missing.");
        }

        Guid? userId = null;

        foreach (var claim in subjectClaims)
        {
            if (!Guid.TryParse(
                    claim.Value, out var parsed) ||
                parsed == Guid.Empty)
            {
                throw new ReviewApiAuthenticationException(
                    "Invalid user identity.");
            }

            if (userId.HasValue &&
                userId.Value != parsed)
            {
                throw new ReviewApiAuthenticationException(
                    "Conflicting identity claims.");
            }

            userId = parsed;
        }

        return userId!.Value;
    }
}

// ===========================================
// 6. SECURE APPLICATION BOUNDARY
// ===========================================

// This is NOT an HTTP endpoint.
//
// The future controller must use a principal
// produced by verified JWT authentication,
// never one constructed from request fields.
//
// RequirementReviewWorkflow checks project
// access using IRequirementReviewAccessPolicy.

public sealed class ReviewApiDecisionBoundary(
    RequirementReviewWorkflow workflow)
{
    public async Task<ReviewApiDecisionReceipt> SubmitAsync(
        ClaimsPrincipal? principal,
        Guid projectId,
        string requirementId,
        string? rawJson,
        CancellationToken cancellationToken = default)
    {
        Guid authenticatedUserId =
            ReviewApiPrincipalReader
                .RequireAuthenticatedUser(principal);

        var body = ReviewApiDecisionJson.Parse(rawJson);

        var command = ReviewApiDecisionMapper.ToCommand(
            projectId,
            requirementId,
            body);

        // Existing workflow:
        // 1. Verify project authorization
        // 2. Load requirement
        // 3. Check revision
        // 4. Apply human decision
        // 5. Persist + audit atomically
        var result = await workflow.SubmitAsync(
            command,
            authenticatedUserId,
            cancellationToken);

        return new ReviewApiDecisionReceipt(
            result.Requirement.Id,
            result.Requirement.Status
                .ToString()
                .ToLowerInvariant(),
            result.Requirement.Revision);
    }
}
