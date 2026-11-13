
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sehatak.Application.Interfaces.IEmail;
using System.Net;
using System.Net.Mail;


namespace Sehatak.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly Microsoft.Extensions.Logging.ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, Microsoft.Extensions.Logging.ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        private async Task SendAsync(string toEmail, string subject, string body)
        {
            using var client = new SmtpClient(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"]!))
            {
                Credentials = new NetworkCredential(
                    _config["Email:Username"],
                    _config["Email:Password"]),
                EnableSsl = true
            };

            var message = new MailMessage
            {
                From = new MailAddress(_config["Email:From"]!, _config["Email:DisplayName"]),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);
        }

        public async Task SendOtpAsync(string toEmail, string code, string purpose)
        {
            var subject = purpose switch
            {
                "register" => "تأكيد حسابك - SIHATUAK",
                "change-email" => "تأكيد تغيير الإيميل - SIHATUAK",
                "change-password" => "تأكيد تغيير كلمة السر - SIHATUAK",
                _ => "رمز التحقق - SIHATUAK"
            };

            var body = $@"
                <div dir='rtl' style='font-family:Arial;padding:20px'>
                    <h2>رمز التحقق الخاص بك</h2>
                    <p style='font-size:32px;font-weight:bold;letter-spacing:8px'>{code}</p>
                </div>";

            await SendAsync(toEmail, subject, body);
        }

        public async Task SendTempPasswordAsync(string toEmail, string name, string tempPassword, string centerName)
        {
            name = WebUtility.HtmlEncode(name);
            centerName = WebUtility.HtmlEncode(centerName);
            var body = $@"
                <div dir='rtl' style='font-family:Arial;padding:20px'>
                    <h2>مرحباً {name}</h2>
                    <p>تم إنشاء حسابك بمركز <strong>{centerName}</strong> على منصة SIHATUAK.</p>
                    <p>كلمة السر المؤقتة الخاصة بك:</p>
                    <p style='font-size:24px;font-weight:bold;background:#f0f0f0;padding:10px;border-radius:8px'>{tempPassword}</p>
                    <p style='color:red'>يرجى تغيير كلمة السر فور تسجيل الدخول.</p>
                </div>";

            await SendAsync(toEmail, "بيانات حسابك - SIHATUAK", body);
        }

        public async Task SendSubscriptionRenewalReminderAsync(string toEmail, string centerName, DateOnly endDate)
        {
            centerName = WebUtility.HtmlEncode(centerName);
            var body = $@"
                <div dir='rtl' style='font-family:Arial;padding:20px'>
                    <h2>تنبيه تجديد اشتراك</h2>
                    <p>اشتراك مركز <strong>{centerName}</strong> سينتهي بتاريخ <strong>{endDate}</strong>.</p>
                    <p>يرجى التواصل مع الإدارة لتجديد الاشتراك.</p>
                </div>";

            await SendAsync(toEmail, $"تنبيه: اشتراك {centerName} سينتهي قريباً", body);
        }


        public async Task SendPaymentConfirmedAsync(string toEmail, string centerName, decimal amount)
        {
            centerName = WebUtility.HtmlEncode(centerName);
            var body = $@"
                <div dir='rtl' style='font-family:Arial;padding:20px'>
                    <h2>تأكيد استلام الدفع</h2>
                    <p>تم استلام دفعة اشتراك مركز <strong>{centerName}</strong>.</p>
                    <p>المبلغ: <strong>{amount} ₪</strong></p>
                    <p>تم تفعيل اشتراكك بنجاح.</p>
                </div>";

            await SendAsync(toEmail, $"تأكيد دفع - {centerName}", body);
        }

        public async Task SendCustomMessageAsync(string toEmail, string subject, string message)
        {
            var body = $@"
        <div dir='rtl' style='font-family:Arial;padding:20px'>
            <p>{message}</p>
            <hr/>
            <small>منصة SIHATUAK</small>
        </div>";

            await SendAsync(toEmail, subject, body);
        }

        public async Task SendBulkAsync(List<string> emails, string subject, string message)
        {
            foreach (var email in emails)
            {
                try
                {
                    await SendCustomMessageAsync(email, subject, message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email to {Email}", email);
                }
            }
        }

        

    }
}
