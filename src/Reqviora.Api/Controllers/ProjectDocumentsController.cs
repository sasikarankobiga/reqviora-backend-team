using System.Security.Claims;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Documents;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Api.Features.ProjectWorkflow;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/documents")]
public sealed class ProjectDocumentsController(ReqvioraDbContext db, IFileStorageService storage) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["text/plain"] = ".txt"
        };

    public sealed record DocumentUploadResponse(
        Guid Id,
        Guid ProjectId,
        string FileName,
        string MediaType,
        long SizeBytes,
        DateTimeOffset CreatedAt);

    [HttpPost, Consumes("multipart/form-data"), RequestSizeLimit(26_214_400)]
    [ProducesResponseType<DocumentUploadResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
                out var actor))
        {
            return Unauthorized();
        }

        await ProjectApiAccess.RequireWriteAsync(db, projectId, actor, ct);

        var safeName = Path.GetFileName(file.FileName);
        var document = new ProjectDocument
        {
            ProjectId = projectId,
            FileName = safeName,
            CreatedBy = actor.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        InputStudioContractValidator.Validate(new DocumentInputRequest(
            projectId,
            document.Id,
            file.FileName,
            file.ContentType,
            file.Length));
        if (!AllowedExtensions.TryGetValue(file.ContentType, out var requiredExtension) ||
            !Path.GetExtension(safeName).Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The document extension does not match its media type.");
        }

        await using var stream = file.OpenReadStream();
        try
        {
            document.StoragePath = await storage.SaveAsync(stream, safeName, ct);
        }
        catch (InvalidDataException)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The uploaded document was rejected.");
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Document security scanning is unavailable.");
        }

        db.ProjectDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return Created(
            $"/api/projects/{projectId}/documents/{document.Id}",
            new DocumentUploadResponse(
                document.Id,
                document.ProjectId,
                document.FileName,
                file.ContentType,
                file.Length,
                document.CreatedAt));
    }
}
