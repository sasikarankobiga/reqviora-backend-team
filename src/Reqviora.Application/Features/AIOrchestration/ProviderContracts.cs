
using System.Text.Json;
using Reqviora.Application.Features.Documents;

namespace Reqviora.Application.Features.AIOrchestration;

public enum AiOperation
{
    RequirementExtraction,
    DocumentSummary,
    ScreenSuggestion
}

public enum AiRunStatus
{
    Completed,
    Unavailable,
    Failed
}

public sealed record AiProviderRequest(
    Guid ProjectId,
    Guid RequestId,
    AiOperation Operation,
    string LanguageCode,
    string InputText
);

public sealed record AiProviderResponse(
    Guid RequestId,
    AiRunStatus Status,
    string? StructuredJson,
    string? ErrorCode
)
{
    // AI output is always a candidate, never an approval.
    public bool RequiresUserConfirmation => true;
}

// G0-07 pending: no concrete provider implementation.
public interface IStructuredAiProvider
{
    Task<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default
    );
}

public static class AiContractValidator
{
    internal const int MaxStructuredJsonLength = 2_000_000;

    public static void Validate(AiProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProjectId == Guid.Empty ||
            request.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Project ID and request ID are required.");
        }

        if (!Enum.IsDefined(request.Operation))
            throw new ArgumentException("Unsupported AI operation.");

        if (!InputStudioContractValidator.IsSupportedLanguage(request.LanguageCode))
        {
            throw new ArgumentException("Unsupported AI language.");
        }

        if (string.IsNullOrWhiteSpace(request.InputText) ||
            request.InputText.Length > 100_000)
        {
            throw new ArgumentException("Invalid AI input text.");
        }
    }

    public static void Validate(AiProviderResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.RequestId == Guid.Empty)
            throw new ArgumentException("AI response request ID is missing.");

        if (!Enum.IsDefined(response.Status))
            throw new ArgumentException("Invalid AI status.");

        if (response.Status == AiRunStatus.Completed)
        {
            if (string.IsNullOrWhiteSpace(response.StructuredJson))
                throw new ArgumentException(
                    "Successful AI response requires structured JSON.");

            if (response.StructuredJson.Length > MaxStructuredJsonLength)
                throw new ArgumentException("AI response is too large.");

            if (!string.IsNullOrEmpty(response.ErrorCode))
                throw new ArgumentException(
                    "Successful AI response cannot contain an error code.");

            try
            {
                using var document = JsonDocument.Parse(
                    response.StructuredJson,
                    new JsonDocumentOptions
                    {
                        AllowDuplicateProperties = false,
                        MaxDepth = 32
                    });

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Object)
                {
                    throw new ArgumentException(
                        "AI output must be a JSON object.");
                }
            }
            catch (JsonException)
            {
                throw new ArgumentException(
                    "AI output is not valid JSON.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(response.ErrorCode))
                throw new ArgumentException(
                    "Failed or unavailable AI response needs an error code.");

            if (!string.IsNullOrWhiteSpace(response.StructuredJson))
                throw new ArgumentException(
                    "Failed AI response cannot contain candidate output.");
        }
    }
}
