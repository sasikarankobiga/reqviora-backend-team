
using Reqviora.Application.Features.Documents;

namespace Reqviora.Application.Features.VoiceProcessing;

public sealed record TranscriptSegment(
    long StartMs,
    long EndMs,
    string Text
);

public sealed record TranscriptDraft(
    Guid TranscriptId,
    Guid AudioFileId,
    string LanguageCode,
    string OriginalText,
    string EditableText,
    int Revision,
    IReadOnlyList<TranscriptSegment> Segments
);

public sealed record EditTranscriptRequest(
    Guid TranscriptId,
    int ExpectedRevision,
    string EditedText
);

public sealed record ReviewTranscriptRequest(
    Guid TranscriptId,
    int ExpectedRevision,
    bool AcknowledgedByUser
);

public interface IVoiceTranscriber
{
    Task<TranscriptDraft> TranscribeAsync(
        VoiceInputRequest request,
        Stream audioContent,
        CancellationToken cancellationToken = default
    );
}

public static class VoiceTranscriptionContractValidator
{
    public static void Validate(TranscriptDraft draft)
    {
        if (draft.TranscriptId == Guid.Empty ||
            draft.AudioFileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Transcript ID and audio file ID are required.");
        }

        if (!InputStudioContractValidator.IsSupportedLanguage(
            draft.LanguageCode))
        {
            throw new ArgumentException(
                "Unsupported transcript language.");
        }

        if (draft.Revision < 1)
            throw new ArgumentException(
                "Transcript revision must start at 1.");

        if (string.IsNullOrWhiteSpace(draft.OriginalText) ||
            string.IsNullOrWhiteSpace(draft.EditableText))
        {
            throw new ArgumentException(
                "Transcript text is required.");
        }

        if (draft.Segments is null)
            throw new ArgumentException(
                "Transcript segments are required.");

        foreach (var segment in draft.Segments)
        {
            if (segment is null ||
                segment.StartMs < 0 ||
                segment.EndMs <= segment.StartMs ||
                string.IsNullOrWhiteSpace(segment.Text))
            {
                throw new ArgumentException(
                    "Invalid transcript segment.");
            }
        }
    }

    public static void Validate(EditTranscriptRequest request)
    {
        if (request.TranscriptId == Guid.Empty ||
            request.ExpectedRevision < 1 ||
            string.IsNullOrWhiteSpace(request.EditedText))
        {
            throw new ArgumentException(
                "Invalid transcript edit request.");
        }
    }

    public static void Validate(ReviewTranscriptRequest request)
    {
        if (request.TranscriptId == Guid.Empty ||
            request.ExpectedRevision < 1 ||
            !request.AcknowledgedByUser)
        {
            throw new ArgumentException(
                "Explicit transcript review acknowledgement is required.");
        }
    }
}
