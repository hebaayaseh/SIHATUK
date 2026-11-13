using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Sehatak.IntegrationTests.Infrastructure;

/// <summary>One mail the fake server received, with the MIME parts already decoded.</summary>
public sealed record ReceivedMail(string From, List<string> Recipients, string Subject, string Body, string? Username, string? Password);

/// <summary>
/// A tiny SMTP server on a random local port: STARTTLS with a self-signed certificate, AUTH LOGIN, MAIL/RCPT/DATA.
/// EmailService always uses SSL, so a plain server would not work. Recipients listed in <see cref="RejectRecipients"/> get a 550.
/// Needs <c>ServicePointManager.ServerCertificateValidationCallback</c> to accept the self-signed certificate.
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _cert;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public ConcurrentQueue<ReceivedMail> Mails { get; } = new();
    public ConcurrentBag<string> RejectRecipients { get; } = new();
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public FakeSmtpServer()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var tmp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        _cert = new X509Certificate2(tmp.Export(X509ContentType.Pfx));   // re-import so the private key is usable by SslStream
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            Stream stream = client.GetStream();
            await WriteAsync(stream, "220 fake-smtp ready");

            string? from = null, user = null, pass = null;
            var rcpt = new List<string>();

            while (true)
            {
                var line = await ReadLineAsync(stream);
                if (line == null) return;
                var up = line.ToUpperInvariant();

                if (up.StartsWith("EHLO") || up.StartsWith("HELO"))
                {
                    if (stream is SslStream) await WriteAsync(stream, "250-fake-smtp\r\n250 AUTH LOGIN");
                    else await WriteAsync(stream, "250-fake-smtp\r\n250 STARTTLS");
                }
                else if (up.StartsWith("STARTTLS"))
                {
                    await WriteAsync(stream, "220 go ahead");
                    var ssl = new SslStream(stream, false);
                    await ssl.AuthenticateAsServerAsync(_cert, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
                    stream = ssl;
                }
                else if (up.StartsWith("AUTH LOGIN"))
                {
                    var rest = line.Length > 10 ? line[10..].Trim() : "";
                    if (rest.Length == 0)
                    {
                        await WriteAsync(stream, "334 VXNlcm5hbWU6");
                        rest = (await ReadLineAsync(stream)) ?? "";
                    }
                    user = Encoding.UTF8.GetString(Convert.FromBase64String(rest));
                    await WriteAsync(stream, "334 UGFzc3dvcmQ6");
                    pass = Encoding.UTF8.GetString(Convert.FromBase64String((await ReadLineAsync(stream)) ?? ""));
                    await WriteAsync(stream, "235 authenticated");
                }
                else if (up.StartsWith("MAIL FROM"))
                {
                    from = Address(line);
                    rcpt.Clear();
                    await WriteAsync(stream, "250 ok");
                }
                else if (up.StartsWith("RCPT TO"))
                {
                    var a = Address(line);
                    if (RejectRecipients.Contains(a, StringComparer.OrdinalIgnoreCase)) await WriteAsync(stream, "550 mailbox unavailable");
                    else { rcpt.Add(a); await WriteAsync(stream, "250 ok"); }
                }
                else if (up == "DATA")
                {
                    await WriteAsync(stream, "354 end with <CRLF>.<CRLF>");
                    var sb = new StringBuilder();
                    while (true)
                    {
                        var l = await ReadLineAsync(stream);
                        if (l == null) return;
                        if (l == ".") break;
                        sb.Append(l.StartsWith("..") ? l[1..] : l).Append("\r\n");
                    }
                    var (subject, body) = Parse(sb.ToString());
                    Mails.Enqueue(new ReceivedMail(from ?? "", rcpt.ToList(), subject, body, user, pass));
                    await WriteAsync(stream, "250 queued");
                }
                else if (up.StartsWith("RSET")) { rcpt.Clear(); await WriteAsync(stream, "250 ok"); }
                else if (up.StartsWith("NOOP")) await WriteAsync(stream, "250 ok");
                else if (up.StartsWith("QUIT")) { await WriteAsync(stream, "221 bye"); return; }
                else await WriteAsync(stream, "502 not implemented");
            }
        }
        catch { /* a client that disconnects early is not a test failure */ }
    }

    private static string Address(string line)
    {
        var m = Regex.Match(line, "<([^>]*)>");
        return m.Success ? m.Groups[1].Value : line;
    }

    private static async Task WriteAsync(Stream s, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text + "\r\n");
        await s.WriteAsync(bytes);
        await s.FlushAsync();
    }

    private static async Task<string?> ReadLineAsync(Stream s)
    {
        var buf = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var n = await s.ReadAsync(one);
            if (n == 0) return buf.Count == 0 ? null : Encoding.UTF8.GetString(buf.ToArray());
            if (one[0] == '\n') break;
            if (one[0] != '\r') buf.Add(one[0]);
        }
        return Encoding.UTF8.GetString(buf.ToArray());
    }

    // ---------- MIME ----------

    private static (string Subject, string Body) Parse(string raw)
    {
        var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerText = split < 0 ? raw : raw[..split];
        var bodyText = split < 0 ? "" : raw[(split + 4)..];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? last = null;
        foreach (var l in headerText.Split("\r\n"))
        {
            if ((l.StartsWith(' ') || l.StartsWith('\t')) && last != null) headers[last] += " " + l.Trim();
            else
            {
                var i = l.IndexOf(':');
                if (i <= 0) continue;
                last = l[..i];
                headers[last] = l[(i + 1)..].Trim();
            }
        }

        var subject = DecodeWords(headers.GetValueOrDefault("Subject", ""));
        var enc = headers.GetValueOrDefault("Content-Transfer-Encoding", "7bit").Trim().ToLowerInvariant();
        var body = enc switch
        {
            "base64" => Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(bodyText, @"\s+", ""))),
            "quoted-printable" => DecodeQuotedPrintable(bodyText),
            _ => bodyText
        };
        return (subject, body);
    }

    private static string DecodeWords(string value) =>
        Regex.Replace(Regex.Replace(value, @"\?=\s+=\?", "?==?"), @"=\?utf-8\?([bBqQ])\?([^?]*)\?=", m =>
            m.Groups[1].Value.ToUpperInvariant() == "B"
                ? Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[2].Value))
                : DecodeQuotedPrintable(m.Groups[2].Value.Replace('_', ' ')), RegexOptions.IgnoreCase);

    private static string DecodeQuotedPrintable(string text)
    {
        text = text.Replace("=\r\n", "");
        var bytes = new List<byte>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '=' && i + 2 < text.Length + 0 && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            { bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16)); i += 2; }
            else bytes.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _cert.Dispose();
    }
}
