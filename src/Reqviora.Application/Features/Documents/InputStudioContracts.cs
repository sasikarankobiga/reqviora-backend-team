
namespace Reqviora.Application.Features.Documents;

public sealed record TextInputRequest(
    Guid ProjectId,
    string Text,
    string LanguageCode
);

public sealed record DocumentInputRequest(
    Guid ProjectId,
    Guid UploadedFileId,
    string FileName,
    string MediaType,
    long SizeBytes
);

public sealed record VoiceInputRequest(
    Guid ProjectId,
    Guid AudioFileId,
    string LanguageCode,
    string MediaType,
    long SizeBytes,
    int DurationSeconds,
    bool ConsentGranted
);

public sealed record ManualRequirementInput(
    string ClientId,
    string Text
);

public sealed record ManualInputRequest(
    Guid ProjectId,
    IReadOnlyList<ManualRequirementInput> Requirements
);

public static class InputStudioContractValidator
{
    public static bool IsSupportedLanguage(string? language) =>
        language is "en" or "en-LK" or "ta" or "ta-LK";

    public static void Validate(TextInputRequest input)
    {
        RequireProject(input.ProjectId);

        if (string.IsNullOrWhiteSpace(input.Text) ||
            input.Text.Length > 100_000)
        {
            throw new ArgumentException(
                "Text must contain 1 to 100000 characters.");
        }

        RequireLanguage(input.LanguageCode);
    }

    public static void Validate(DocumentInputRequest input)
    {
        RequireProject(input.ProjectId);

        if (input.UploadedFileId == Guid.Empty)
            throw new ArgumentException("Uploaded file ID is required.");

        if (string.IsNullOrWhiteSpace(input.FileName) ||
            input.FileName.Contains('/') ||
            input.FileName.Contains('\\'))
        {
            throw new ArgumentException("Invalid document file name.");
        }

        if (input.MediaType is not (
            "application/pdf" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "text/plain"))
        {
            throw new ArgumentException("Unsupported document media type.");
        }

        if (input.SizeBytes <= 0 || input.SizeBytes > 25_000_000)
            throw new ArgumentException("Invalid document file size.");
    }

    public static void Validate(VoiceInputRequest input)
    {
        RequireProject(input.ProjectId);

        if (input.AudioFileId == Guid.Empty)
            throw new ArgumentException("Audio file ID is required.");

        RequireLanguage(input.LanguageCode);

        if (!input.ConsentGranted)
            throw new ArgumentException("Voice processing consent is required.");

        if (input.MediaType is not (
            "audio/wav" or "audio/mpeg" or
            "audio/mp4" or "audio/webm"))
        {
            throw new ArgumentException("Unsupported audio media type.");
        }

        if (input.SizeBytes <= 0 || input.SizeBytes > 25_000_000)
            throw new ArgumentException("Invalid audio file size.");

        if (input.DurationSeconds <= 0 || input.DurationSeconds > 1800)
            throw new ArgumentException("Invalid audio duration.");
    }

    public static void Validate(ManualInputRequest input)
    {
        RequireProject(input.ProjectId);

        if (input.Requirements is null ||
            input.Requirements.Count == 0 ||
            input.Requirements.Count > 200)
        {
            throw new ArgumentException(
                "Provide between 1 and 200 manual requirements.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var requirement in input.Requirements)
        {
            if (requirement is null ||
                string.IsNullOrWhiteSpace(requirement.ClientId) ||
                string.IsNullOrWhiteSpace(requirement.Text) ||
                requirement.Text.Length > 5000)
            {
                throw new ArgumentException(
                    "Invalid manual requirement.");
            }

            if (!ids.Add(requirement.ClientId))
                throw new ArgumentException(
                    "Duplicate manual requirement client ID.");
        }
    }

    private static void RequireProject(Guid projectId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project ID is required.");
    }

    private static void RequireLanguage(string? language)
    {
        if (!IsSupportedLanguage(language))
            throw new ArgumentException(
                "Supported languages: en, en-LK, ta, ta-LK.");
    }
}
