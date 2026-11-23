using System.Text.Json;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AIOrchestration;

namespace Reqviora.Infrastructure.Services;

public sealed class StructuredAiProvider(IAIService ai) : IStructuredAiProvider
{
    public async Task<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        AiContractValidator.Validate(request);
        var instruction = request.Operation switch
        {
            AiOperation.RequirementExtraction => """
                Return only JSON using this exact shape:
                {"contractVersion":"1.0","candidates":[{"id":"REQ-...","title":"...","description":"...","languageCode":"en|en-LK|ta|ta-LK","confidence":0.0,"evidence":[{"sourceId":"...","locator":"...","quote":"..."}]}]}.
                Every evidence value must be copied exactly from the supplied input. Do not approve requirements.
                """,
            AiOperation.DocumentSummary => "Return only a JSON object containing a concise summary and keyPoints array.",
            AiOperation.ScreenSuggestion => "Return only a Blueprint v1 JSON object. Preserve confirmed requirements exactly.",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        try
        {
            var output = await ai.CompleteAsync(
                $"{instruction}\nLanguage: {request.LanguageCode}\nInput:\n{request.InputText}", cancellationToken);
            using var parsed = JsonDocument.Parse(output, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = 32
            });
            var response = new AiProviderResponse(request.RequestId, AiRunStatus.Completed, parsed.RootElement.GetRawText(), null);
            AiContractValidator.Validate(response);
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException)
        {
            return new(request.RequestId, AiRunStatus.Unavailable, null, "AI_PROVIDER_UNAVAILABLE");
        }
        catch (Exception)
        {
            return new(request.RequestId, AiRunStatus.Failed, null, "AI_PROVIDER_FAILED");
        }
    }
}
