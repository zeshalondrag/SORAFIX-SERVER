using MailKit.Net.Smtp;
using MimeKit;

namespace sorafix_api.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string email, string subject, string message)
    {
        var settings = _config.GetSection("EmailSettings");
        var emailMessage = new MimeMessage();

        emailMessage.From.Add(new MailboxAddress(settings["SenderName"], settings["SenderEmail"]));
        emailMessage.To.Add(MailboxAddress.Parse(email));
        emailMessage.Subject = subject;
        emailMessage.Body = new TextPart("html") { Text = message };

        using var client = new SmtpClient();
        await client.ConnectAsync(settings["SmtpServer"], int.Parse(settings["Port"]!), MailKit.Security.SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(settings["Username"], settings["Password"]);
        await client.SendAsync(emailMessage);
        await client.DisconnectAsync(true);
    }
}