
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.VoiceProcessing;

var projectId = Guid.NewGuid();
var fileId = Guid.NewGuid();
var requestId = Guid.NewGuid();

var text = new TextInputRequest(
    projectId, "User needs a login screen.", "en");

var document = new DocumentInputRequest(
    projectId, fileId, "requirements.pdf",
    "application/pdf", 10000);

var voice = new VoiceInputRequest(
    projectId, fileId, "ta-LK", "audio/wav",
    10000, 30, true);

var manual = new ManualInputRequest(
    projectId,
    [new ManualRequirementInput(
        "M-001", "User must log in.")]);

var aiRequest = new AiProviderRequest(
    projectId, requestId,
    AiOperation.RequirementExtraction,
    "en", "Extract requirement candidates.");

var aiResponse = new AiProviderResponse(
    requestId, AiRunStatus.Completed,
    """{"candidates":[]}""", null);

var evidence = new SourceEvidenceRef(
    "SRC-001", "page 1 paragraph 1",
    1, 0, 14, "User must login");

var parsedDocument = new DocumentParseResult(
    fileId,
    [new ParsedDocumentSegment(
        0, "User must login", evidence)],
    Array.Empty<string>());

var transcript = new TranscriptDraft(
    Guid.NewGuid(), fileId, "ta-LK",
    "எனக்கு login screen வேண்டும்",
    "எனக்கு login screen வேண்டும்",
    1,
    [new TranscriptSegment(
        0, 2000, "எனக்கு login screen வேண்டும்")]
);

var edit = new EditTranscriptRequest(
    transcript.TranscriptId, 1,
    "எனக்கு login மற்றும் home screen வேண்டும்");

var review = new ReviewTranscriptRequest(
    transcript.TranscriptId, 1, true);

int passed = 0;
int failed = 0;

void Test(string name, Action action)
{
    try
    {
        action();
        passed++;
        Console.WriteLine("[PASS] " + name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("[FAIL] " + name);
        Console.WriteLine("       " + ex.Message);
    }
}

void Reject(string name, Action action)
{
    Test(name, () =>
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Expected validation to reject invalid input.");
    });
}

// TASK 4: INPUT STUDIO

Test("Valid English text", () =>
    InputStudioContractValidator.Validate(text));

Test("Valid Tamil text", () =>
    InputStudioContractValidator.Validate(
        text with { LanguageCode = "ta-LK" }));

Test("Valid document metadata", () =>
    InputStudioContractValidator.Validate(document));

Test("Valid Tamil voice input", () =>
    InputStudioContractValidator.Validate(voice));

Test("Valid manual input", () =>
    InputStudioContractValidator.Validate(manual));

Reject("Empty text rejected", () =>
    InputStudioContractValidator.Validate(
        text with { Text = "" }));

Reject("Invalid language rejected", () =>
    InputStudioContractValidator.Validate(
        text with { LanguageCode = "fr" }));

Reject("Missing project ID rejected", () =>
    InputStudioContractValidator.Validate(
        text with { ProjectId = Guid.Empty }));

Reject("Unsafe filename rejected", () =>
    InputStudioContractValidator.Validate(
        document with { FileName = "../secret.txt" }));

Reject("Unsupported document type rejected", () =>
    InputStudioContractValidator.Validate(
        document with { MediaType = "application/x-executable" }));

Reject("Oversized document rejected", () =>
    InputStudioContractValidator.Validate(
        document with { SizeBytes = 30_000_000 }));

Reject("Voice without consent rejected", () =>
    InputStudioContractValidator.Validate(
        voice with { ConsentGranted = false }));

Reject("Unsupported audio rejected", () =>
    InputStudioContractValidator.Validate(
        voice with { MediaType = "video/mp4" }));

Reject("Invalid audio duration rejected", () =>
    InputStudioContractValidator.Validate(
        voice with { DurationSeconds = 0 }));

Reject("Duplicate manual ID rejected", () =>
    InputStudioContractValidator.Validate(
        manual with
        {
            Requirements =
            [
                new("M-001", "First requirement"),
                new("M-001", "Second requirement")
            ]
        }));

Reject("Empty manual list rejected", () =>
    InputStudioContractValidator.Validate(
        manual with
        {
            Requirements = Array.Empty<ManualRequirementInput>()
        }));

// TASK 5: AI PROVIDER CONTRACT

Test("Valid AI request", () =>
    AiContractValidator.Validate(aiRequest));

Test("Valid AI response", () =>
    AiContractValidator.Validate(aiResponse));

Test("AI always needs human confirmation", () =>
{
    if (!aiResponse.RequiresUserConfirmation)
        throw new Exception("AI must not auto-approve.");
});

Test("Provider interface can use a mock", () =>
{
    IStructuredAiProvider provider = new FakeAiProvider();

    var response = provider.ExecuteAsync(
        aiRequest).GetAwaiter().GetResult();

    AiContractValidator.Validate(response);
});

Reject("Empty AI input rejected", () =>
    AiContractValidator.Validate(
        aiRequest with { InputText = "" }));

Reject("Invalid AI operation rejected", () =>
    AiContractValidator.Validate(
        aiRequest with { Operation = (AiOperation)999 }));

Reject("Invalid AI JSON rejected", () =>
    AiContractValidator.Validate(
        aiResponse with { StructuredJson = "not-json" }));

Reject("Array AI output rejected", () =>
    AiContractValidator.Validate(
        aiResponse with { StructuredJson = "[]" }));

Reject("Empty successful AI output rejected", () =>
    AiContractValidator.Validate(
        aiResponse with { StructuredJson = "" }));

Reject("Failure without error code rejected", () =>
    AiContractValidator.Validate(
        aiResponse with
        {
            Status = AiRunStatus.Failed,
            StructuredJson = null,
            ErrorCode = null
        }));

// TASK 6: DOCUMENT INTELLIGENCE

Test("Valid source evidence", () =>
    DocumentParsingContractValidator.Validate(parsedDocument));

Test("Document evidence mapper preserves provenance in source order", () =>
{
    var second = evidence with
    {
        Locator = "page 2 paragraph 1",
        PageNumber = 2,
        StartOffset = 20,
        EndOffset = 35
    };
    ISourceEvidenceMapper mapper = new DocumentSourceEvidenceMapper();
    var sources = mapper.Map(parsedDocument with
    {
        Segments =
        [
            new(1, "User must login", second),
            parsedDocument.Segments[0]
        ]
    });

    if (!sources.SequenceEqual([evidence, second]))
        throw new Exception("Expected source IDs, locations, offsets and quotes to be preserved.");
});

Reject("Duplicate segment sequence rejected", () =>
    DocumentParsingContractValidator.Validate(
        parsedDocument with
        {
            Segments =
            [
                parsedDocument.Segments[0],
                parsedDocument.Segments[0]
            ]
        }));

Reject("Missing evidence locator rejected", () =>
    DocumentParsingContractValidator.Validate(
        parsedDocument with
        {
            Segments =
            [
                new ParsedDocumentSegment(
                    0, "User must login",
                    evidence with { Locator = "" })
            ]
        }));

Reject("Evidence quote mismatch rejected", () =>
    DocumentParsingContractValidator.Validate(
        parsedDocument with
        {
            Segments =
            [
                new ParsedDocumentSegment(
                    0, "User must login",
                    evidence with { Quote = "Unknown text" })
            ]
        }));

Reject("Invalid source page rejected", () =>
    DocumentParsingContractValidator.Validate(
        parsedDocument with
        {
            Segments =
            [
                new ParsedDocumentSegment(
                    0, "User must login",
                    evidence with { PageNumber = 0 })
            ]
        }));

// TASK 7: VOICE TRANSCRIPTION

Test("Valid Tamil transcript draft", () =>
    VoiceTranscriptionContractValidator.Validate(transcript));

Test("Valid transcript edit", () =>
    VoiceTranscriptionContractValidator.Validate(edit));

Test("Valid explicit transcript review", () =>
    VoiceTranscriptionContractValidator.Validate(review));

Reject("Invalid transcript timing rejected", () =>
    VoiceTranscriptionContractValidator.Validate(
        transcript with
        {
            Segments =
            [
                new TranscriptSegment(3000, 1000, "Invalid")
            ]
        }));

Reject("Unsupported transcript language rejected", () =>
    VoiceTranscriptionContractValidator.Validate(
        transcript with { LanguageCode = "xx" }));

Reject("Empty transcript edit rejected", () =>
    VoiceTranscriptionContractValidator.Validate(
        edit with { EditedText = "" }));

Reject("Review without acknowledgement rejected", () =>
    VoiceTranscriptionContractValidator.Validate(
        review with { AcknowledgedByUser = false }));

Console.WriteLine();
Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

return failed == 0 ? 0 : 1;

sealed class FakeAiProvider : IStructuredAiProvider
{
    public Task<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            new AiProviderResponse(
                request.RequestId,
                AiRunStatus.Completed,
                """{"candidates":[]}""",
                null
            )
        );
    }
}
