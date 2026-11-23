namespace Reqviora.Infrastructure.Services;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "REQVIORA";
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string RootPath { get; set; } = "./private-artifacts";
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;
    public int RetentionDays { get; set; } = 365;
    public string ClamAvHost { get; set; } = "localhost";
    public int ClamAvPort { get; set; } = 3310;
    public bool RequireAntivirus { get; set; }
}

public sealed class AiOptions
{
    public const string SectionName = "AI";
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAICompatible";
    public string Endpoint { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5-mini";
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public int TimeoutSeconds { get; set; } = 120;
}
