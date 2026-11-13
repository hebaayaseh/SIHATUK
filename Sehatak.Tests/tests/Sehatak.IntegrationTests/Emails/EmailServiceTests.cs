using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sehatak.Infrastructure.Services;
using Shouldly;

namespace Sehatak.IntegrationTests.Infrastructure;

/// <summary>
/// EmailService talks to a real (local) SMTP server: <see cref="FakeSmtpServer"/> speaks STARTTLS + AUTH LOGIN like a real provider,
/// so the subject, body, recipient, sender and credentials are checked exactly as a mail client would receive them. No Docker needed.
/// </summary>
public class EmailServiceTests
{
    static EmailServiceTests()
    {
#pragma warning disable SYSLIB0014
        ServicePointManager.ServerCertificateValidationCallback = (_, _, _, _) => true;   // the fake server's certificate is self-signed
#pragma warning restore SYSLIB0014
    }

    private static EmailService NewSvc(FakeSmtpServer server) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Email:SmtpHost"] = "127.0.0.1", ["Email:SmtpPort"] = server.Port.ToString(),
        ["Email:Username"] = "mailer", ["Email:Password"] = "s3cret",
        ["Email:From"] = "noreply@sihatuak.test", ["Email:DisplayName"] = "SIHATUAK"
    }).Build(), NullLogger<EmailService>.Instance);

    // ---------- one mail per kind ----------

    [Theory]
    [InlineData("register", "تأكيد حسابك - SIHATUAK")]
    [InlineData("change-email", "تأكيد تغيير الإيميل - SIHATUAK")]
    [InlineData("change-password", "تأكيد تغيير كلمة السر - SIHATUAK")]
    [InlineData("anything-else", "رمز التحقق - SIHATUAK")]
    public async Task The_code_mail_has_the_subject_of_its_purpose_and_the_code_in_the_body(string purpose, string subject)
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendOtpAsync("user@test.local", "482915", purpose);

        var mail = server.Mails.Single();
        mail.Subject.ShouldBe(subject);
        mail.Recipients.ShouldBe(new[] { "user@test.local" });
        mail.Body.ShouldContain("482915");
        mail.From.ShouldBe("noreply@sihatuak.test");
    }

    [Fact]
    public async Task The_mail_is_sent_with_the_configured_login()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendOtpAsync("user@test.local", "123456", "register");

        var mail = server.Mails.Single();
        mail.Username.ShouldBe("mailer");
        mail.Password.ShouldBe("s3cret");
    }

    [Fact]
    public async Task The_temporary_password_mail_has_the_name_the_center_and_the_password()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendTempPasswordAsync("nurse@test.local", "Dina", "Tmp#4821", "Al-Amal Clinic");

        var mail = server.Mails.Single();
        mail.Subject.ShouldBe("بيانات حسابك - SIHATUAK");
        mail.Recipients.ShouldBe(new[] { "nurse@test.local" });
        mail.Body.ShouldContain("Dina");
        mail.Body.ShouldContain("Al-Amal Clinic");
        mail.Body.ShouldContain("Tmp#4821");
    }

    [Fact]
    public async Task The_renewal_reminder_names_the_center_and_the_end_date()
    {
        using var server = new FakeSmtpServer();
        var end = new DateOnly(2027, 3, 15);

        await NewSvc(server).SendSubscriptionRenewalReminderAsync("admin@test.local", "Al-Amal Clinic", end);

        var mail = server.Mails.Single();
        mail.Subject.ShouldBe("تنبيه: اشتراك Al-Amal Clinic سينتهي قريباً");
        mail.Body.ShouldContain("Al-Amal Clinic");
        mail.Body.ShouldContain(end.ToString());
    }

    [Fact]
    public async Task The_payment_mail_names_the_center_and_the_amount()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendPaymentConfirmedAsync("admin@test.local", "Al-Amal Clinic", 150.5m);

        var mail = server.Mails.Single();
        mail.Subject.ShouldBe("تأكيد دفع - Al-Amal Clinic");
        mail.Body.ShouldContain("Al-Amal Clinic");
        mail.Body.ShouldContain(150.5m.ToString());
    }

    [Fact]
    public async Task A_custom_message_keeps_its_subject_and_is_signed_by_the_platform()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendCustomMessageAsync("admin@test.local", "Maintenance tonight", "We will be down from 22:00.");

        var mail = server.Mails.Single();
        mail.Subject.ShouldBe("Maintenance tonight");
        mail.Body.ShouldContain("We will be down from 22:00.");
        mail.Body.ShouldContain("SIHATUAK");
    }

    [Fact]
    public async Task Html_in_a_custom_message_is_sent_as_html_because_only_the_super_admin_writes_it()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendCustomMessageAsync("admin@test.local", "News", "<b>Important</b>");

        server.Mails.Single().Body.ShouldContain("<b>Important</b>");
    }

    // ---------- bulk ----------

    [Fact]
    public async Task Bulk_sends_one_separate_mail_per_address_so_nobody_sees_the_others()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendBulkAsync(new List<string> { "a@test.local", "b@test.local", "c@test.local" }, "Hello", "Body");

        var mails = server.Mails.ToList();
        mails.Count.ShouldBe(3);
        mails.ShouldAllBe(m => m.Recipients.Count == 1);
        mails.SelectMany(m => m.Recipients).OrderBy(x => x).ShouldBe(new[] { "a@test.local", "b@test.local", "c@test.local" });
        mails.ShouldAllBe(m => m.Subject == "Hello");
    }

    [Fact]
    public async Task Bulk_goes_on_with_the_next_address_when_one_is_rejected_and_never_throws()
    {
        using var server = new FakeSmtpServer();
        server.RejectRecipients.Add("bad@test.local");

        await NewSvc(server).SendBulkAsync(new List<string> { "a@test.local", "bad@test.local", "c@test.local" }, "Hello", "Body");

        server.Mails.SelectMany(m => m.Recipients).OrderBy(x => x).ShouldBe(new[] { "a@test.local", "c@test.local" });
    }

    [Fact]
    public async Task Bulk_with_nobody_to_mail_sends_nothing()
    {
        using var server = new FakeSmtpServer();

        await NewSvc(server).SendBulkAsync(new List<string>(), "Hello", "Body");

        server.Mails.ShouldBeEmpty();
    }

    // ---------- failures ----------

    [Fact]
    public async Task A_single_mail_to_a_rejected_address_throws_so_the_caller_knows()
    {
        using var server = new FakeSmtpServer();
        server.RejectRecipients.Add("bad@test.local");

        await Should.ThrowAsync<SmtpFailedRecipientException>(() => NewSvc(server).SendOtpAsync("bad@test.local", "123456", "register"));

        server.Mails.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_smtp_setting_fails_loudly_instead_of_pretending_the_mail_was_sent()
    {
        var svc = new EmailService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = "127.0.0.1"
        }).Build(), NullLogger<EmailService>.Instance);

        await Should.ThrowAsync<Exception>(() => svc.SendOtpAsync("user@test.local", "123456", "register"));
    }

    // ---------- html injection ----------

    /// <summary>Names typed by users (staff name, center name) go into the HTML body as they are. A center called
    /// &lt;a href="http://evil"&gt;Click here&lt;/a&gt; puts a real link into the mails the platform sends to the center's staff and admin.</summary>
    [Theory]
    [InlineData("temp-name")]
    [InlineData("temp-center")]
    [InlineData("renewal-center")]
    [InlineData("payment-center")]
    [Trait("Category", "KnownBug")]
    public async Task Names_typed_by_users_cannot_inject_html_into_the_mail(string which)
    {
        using var server = new FakeSmtpServer();
        const string evil = "<a href=\"http://evil.example\">Click here</a>";
        var svc = NewSvc(server);

        switch (which)
        {
            case "temp-name": await svc.SendTempPasswordAsync("u@test.local", evil, "Tmp#1234", "Clinic"); break;
            case "temp-center": await svc.SendTempPasswordAsync("u@test.local", "Dina", "Tmp#1234", evil); break;
            case "renewal-center": await svc.SendSubscriptionRenewalReminderAsync("u@test.local", evil, new DateOnly(2027, 1, 1)); break;
            case "payment-center": await svc.SendPaymentConfirmedAsync("u@test.local", evil, 10m); break;
        }

        var body = server.Mails.Single().Body;
        body.ShouldNotContain("<a href");
        body.ShouldContain("&lt;a href");
    }
}
