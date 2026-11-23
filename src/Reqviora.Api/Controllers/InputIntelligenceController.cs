using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.VoiceProcessing;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/input-intelligence")]
public sealed class InputIntelligenceController(ReqvioraDbContext db, IOptions<AiOptions> options) : ControllerBase
{
    public sealed record ExtractRequirementsRequest(
        string LanguageCode,
        string InputText,
        IReadOnlyList<SourceEvidenceRef> Evidence,
        bool ConsentGranted);

    public sealed class TranscribeRequest
    {
        [Required]
        public IFormFile Audio { get; init; } = null!;

        [Required, StringLength(10)]
        public string LanguageCode { get; init; } = string.Empty;

        [Range(1, 1800)]
        public int DurationSeconds { get; init; }

        public bool ConsentGranted { get; init; }
    }

    [HttpPost("requirements/extract")]
    public async Task<IActionResult> ExtractRequirements(
        Guid projectId,
        ExtractRequirementsRequest body,
        [FromServices] IStructuredAiProvider provider,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        var request = new AiProviderRequest(
            projectId, Guid.NewGuid(), AiOperation.RequirementExtraction, body.LanguageCode, body.InputText);
        AiContractValidator.Validate(request);
        if (!body.ConsentGranted)
            throw new ArgumentException("AI text extraction consent is required.", nameof(body));
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        var response = await provider.ExecuteAsync(request, cancellationToken);
        if (response.Status != AiRunStatus.Completed)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { response.ErrorCode });
        return Ok(RequirementExtractionValidator.Parse(request, response, body.Evidence));
    }

    [HttpPost("voice/transcribe")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> Transcribe(
        Guid projectId,
        [FromForm] TranscribeRequest body,
        [FromServices] IVoiceTranscriber transcriber,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        var request = new VoiceInputRequest(
            projectId,
            Guid.NewGuid(),
            body.LanguageCode,
            body.Audio.ContentType,
            body.Audio.Length,
            body.DurationSeconds,
            body.ConsentGranted);
        InputStudioContractValidator.Validate(request);
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        await using var stream = body.Audio.OpenReadStream();
        return Ok(await transcriber.TranscribeAsync(request, stream, cancellationToken));
    }
}
