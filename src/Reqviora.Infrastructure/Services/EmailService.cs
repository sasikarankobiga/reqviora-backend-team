using Reqviora.Application.Common.Interfaces;

using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Reqviora.Infrastructure.Services;

public sealed class EmailService(IOptions<SmtpOptions> options) : IEmailService
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.Host) || string.IsNullOrWhiteSpace(value.FromAddress))
            throw new InvalidOperationException("SMTP host and sender must be configured through secrets or environment variables.");

        using var message = new MailMessage
        {
            From = new MailAddress(value.FromAddress, value.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(to));
        using var client = new SmtpClient(value.Host, value.Port)
        {
            EnableSsl = value.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(value.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(value.UserName, value.Password)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
