using Reqviora.Application.Common.Interfaces;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reqviora.Infrastructure.Services;

public sealed class AIService(HttpClient http, IOptions<AiOptions> options) : IAIService
{
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ApiKey))
            throw new InvalidOperationException("AI API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = value.Model,
                messages = new[] { new { role = "user", content = prompt } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.ApiKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidDataException("AI provider returned an empty completion.");
    }
}
