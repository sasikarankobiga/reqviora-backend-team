using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.RequirementReview;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId}/requirements/{requirementId}/review")]
public sealed class RequirementReviewsController(ReviewApiDecisionBoundary review) : ControllerBase
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ReviewApiDecisionReceipt>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<ReviewApiDecisionReceipt>> Submit(
        Guid projectId,
        string requirementId,
        CancellationToken cancellationToken)
    {
        if (Request.ContentLength > ReviewApiDecisionJson.MaxBodyBytes)
            return PayloadTooLarge();

        // Preserve the raw JSON so the Phase 1 parser can reject duplicate fields.
        // Read at most one byte over the limit, including for chunked requests.
        var buffer = new byte[ReviewApiDecisionJson.MaxBodyBytes + 1];
        var bytesRead = await Request.Body.ReadAtLeastAsync(
            buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (bytesRead > ReviewApiDecisionJson.MaxBodyBytes)
            return PayloadTooLarge();

        try
        {
            var rawJson = StrictUtf8.GetString(buffer, 0, bytesRead);
            return Ok(await review.SubmitAsync(User, projectId, requirementId, rawJson, cancellationToken));
        }
        catch (DecoderFallbackException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Review JSON must be UTF-8.");
        }
        catch (ReviewApiValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: exception.Message);
        }
        catch (ReviewApiAuthenticationException)
        {
            return Challenge();
        }
        catch (RequirementReviewAccessDeniedException)
        {
            return Forbid();
        }
        catch (RequirementReviewNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: exception.Message);
        }
        catch (RequirementReviewConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);
        }
    }

    private ObjectResult PayloadTooLarge() =>
        Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Review request body is too large.");
}
