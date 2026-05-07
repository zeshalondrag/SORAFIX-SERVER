using Resend;
using Microsoft.Extensions.Configuration;

namespace sorafix_api.Services;

public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _config = config;
    }

    public async Task SendEmailAsync(string email, string subject, string message)
    {
        var senderEmail = _config["EmailSettings:SenderEmail"] ?? "onboarding@resend.dev";
        var senderName = _config["EmailSettings:SenderName"] ?? "SORAFIX";

        var mail = new EmailMessage();
        mail.From = $"{senderName} <{senderEmail}>";
        mail.To.Add(email);
        mail.Subject = subject;
        mail.HtmlBody = message;

        await _resend.EmailSendAsync(mail);
    }
}
