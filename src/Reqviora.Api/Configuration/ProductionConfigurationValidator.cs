namespace Reqviora.Api.Configuration;

public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        Require(configuration.GetValue<bool>("Storage:RequireAntivirus"),
            "Production requires Storage:RequireAntivirus=true.");
        Require(!string.IsNullOrWhiteSpace(configuration["Storage:ClamAvHost"]) &&
                configuration.GetValue<int>("Storage:ClamAvPort") is > 0 and <= 65535,
            "Production requires a valid ClamAV host and port.");
        Require(Path.IsPathRooted(configuration["Storage:RootPath"] ?? string.Empty),
            "Production requires an absolute private Storage:RootPath.");

        RequireSecureUrl(configuration["AccountLinks:BaseUrl"], "AccountLinks:BaseUrl");
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        Require(origins.Length > 0, "Production requires at least one explicit Cors:AllowedOrigins entry.");
        foreach (var origin in origins)
            RequireSecureUrl(origin, "Cors:AllowedOrigins");

        Require(!string.IsNullOrWhiteSpace(configuration["Smtp:Host"]),
            "Production requires Smtp:Host.");
        Require(configuration.GetValue<int>("Smtp:Port") is > 0 and <= 65535,
            "Production requires a valid Smtp:Port.");
        Require(configuration.GetValue<bool>("Smtp:UseSsl"),
            "Production requires SMTP TLS.");
        Require(!string.IsNullOrWhiteSpace(configuration["Smtp:UserName"]) &&
                !string.IsNullOrWhiteSpace(configuration["Smtp:Password"]) &&
                !string.IsNullOrWhiteSpace(configuration["Smtp:FromAddress"]),
            "Production requires secret-backed SMTP credentials and a sender address.");

        if (configuration.GetValue<bool>("AI:Enabled"))
        {
            RequireSecureUrl(configuration["AI:Endpoint"], "AI:Endpoint");
            Require(!string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]),
                "AI:Enabled requires a secret-backed API key after G0-07 approval.");
        }
    }

    private static void RequireSecureUrl(string? value, string name)
    {
        Require(Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo),
            $"Production requires {name} to be a non-loopback HTTPS URL without embedded credentials.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
