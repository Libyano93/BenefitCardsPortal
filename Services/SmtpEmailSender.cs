using System.Net;
using System.Net.Mail;
using System.Text;

namespace BenefitCardsPortal.Services;

public sealed class SmtpEmailSender(ISmtpSettingsStore settingsStore) : ISmtpEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.IsConfigured) throw new InvalidOperationException("أكمل إعدادات خادم SMTP وبيانات المرسل أولًا.");
        using var message = new MailMessage(settings.FromAddress, recipient)
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        if (!string.IsNullOrWhiteSpace(settings.UserName)) client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
#pragma warning disable SYSLIB0014
        await client.SendMailAsync(message).WaitAsync(cancellationToken);
#pragma warning restore SYSLIB0014
    }
}
