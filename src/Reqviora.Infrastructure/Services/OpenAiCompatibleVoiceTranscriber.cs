using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.VoiceProcessing;

namespace Reqviora.Infrastructure.Services;

public sealed class OpenAiCompatibleVoiceTranscriber(
    HttpClient http,
    IOptions<AiOptions> options) : IVoiceTranscriber
{
    public async Task<TranscriptDraft> TranscribeAsync(
        VoiceInputRequest request,
        Stream audioContent,
        CancellationToken cancellationToken = default)
    {
        InputStudioContractValidator.Validate(request);
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ApiKey))
            throw new InvalidOperationException("AI API key is not configured.");
        using var form = new MultipartFormDataContent();
        using var audio = new StreamContent(audioContent);
        audio.Headers.ContentType = MediaTypeHeaderValue.Parse(request.MediaType);
        form.Add(audio, "file", "audio" + Extension(request.MediaType));
        form.Add(new StringContent(value.TranscriptionModel), "model");
        form.Add(new StringContent(request.LanguageCode.StartsWith("ta", StringComparison.Ordinal) ? "ta" : "en"), "language");
        form.Add(new StringContent("verbose_json"), "response_format");
        using var message = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = form };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.ApiKey);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var text = json.RootElement.GetProperty("text").GetString()?.Trim()
            ?? throw new InvalidDataException("Transcription provider returned no text.");
        var segments = new List<TranscriptSegment>();
        if (json.RootElement.TryGetProperty("segments", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var start = item.GetProperty("start").GetDouble();
                var end = item.GetProperty("end").GetDouble();
                var segmentText = item.GetProperty("text").GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(segmentText) && end > start)
                    segments.Add(new((long)(start * 1000), (long)(end * 1000), segmentText));
            }
        }
        if (segments.Count == 0)
            segments.Add(new(0, Math.Max(1, request.DurationSeconds) * 1000L, text));
        var draft = new TranscriptDraft(
            Guid.NewGuid(), request.AudioFileId, request.LanguageCode, text, text, 1, segments);
        VoiceTranscriptionContractValidator.Validate(draft);
        return draft;
    }

    private static string Extension(string mediaType) => mediaType switch
    {
        "audio/wav" => ".wav",
        "audio/mpeg" => ".mp3",
        "audio/mp4" => ".m4a",
        "audio/webm" => ".webm",
        _ => throw new ArgumentException("Unsupported audio media type.")
    };
}
