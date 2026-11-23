
using Reqviora.Application.Features.Documents;

namespace Reqviora.Application.Features.DocumentIntelligence;

public sealed record SourceEvidenceRef(
    string SourceId,
    string Locator,
    int? PageNumber,
    int StartOffset,
    int EndOffset,
    string Quote
);

public sealed record ParsedDocumentSegment(
    int Sequence,
    string Text,
    SourceEvidenceRef Evidence
);

public sealed record DocumentParseResult(
    Guid UploadedFileId,
    IReadOnlyList<ParsedDocumentSegment> Segments,
    IReadOnlyList<string> Warnings
);

public interface IDocumentTextParser
{
    Task<DocumentParseResult> ParseAsync(
        DocumentInputRequest document,
        Stream fileContent,
        CancellationToken cancellationToken = default
    );
}

public interface ISourceEvidenceMapper
{
    IReadOnlyList<SourceEvidenceRef> Map(DocumentParseResult result);
}

public sealed class DocumentSourceEvidenceMapper : ISourceEvidenceMapper
{
    public IReadOnlyList<SourceEvidenceRef> Map(DocumentParseResult result)
    {
        DocumentParsingContractValidator.Validate(result);

        return result.Segments
            .OrderBy(segment => segment.Sequence)
            .Select(segment => segment.Evidence)
            .ToArray();
    }
}

public static class DocumentParsingContractValidator
{
    public static void Validate(DocumentParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.UploadedFileId == Guid.Empty)
            throw new ArgumentException(
                "Document upload ID is required.");

        if (result.Segments is null ||
            result.Warnings is null)
        {
            throw new ArgumentException(
                "Document segments and warnings are required.");
        }

        if (result.Segments.Count > 100_000)
            throw new ArgumentException(
                "Too many document segments.");

        var sequences = new HashSet<int>();

        foreach (var segment in result.Segments)
        {
            if (segment is null ||
                segment.Sequence < 0 ||
                string.IsNullOrWhiteSpace(segment.Text))
            {
                throw new ArgumentException(
                    "Invalid parsed document segment.");
            }

            if (!sequences.Add(segment.Sequence))
                throw new ArgumentException(
                    "Duplicate document segment sequence.");

            var evidence = segment.Evidence;

            if (evidence is null ||
                string.IsNullOrWhiteSpace(evidence.SourceId) ||
                string.IsNullOrWhiteSpace(evidence.Locator) ||
                string.IsNullOrWhiteSpace(evidence.Quote))
            {
                throw new ArgumentException(
                    "Source evidence is required.");
            }

            if (evidence.PageNumber is <= 0 ||
                evidence.StartOffset < 0 ||
                evidence.EndOffset <= evidence.StartOffset)
            {
                throw new ArgumentException(
                    "Invalid evidence location.");
            }

            if (!segment.Text.Contains(
                evidence.Quote, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Evidence quote must appear in its source segment.");
            }
        }
    }
}
