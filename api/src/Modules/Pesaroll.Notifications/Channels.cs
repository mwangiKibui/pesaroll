using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pesaroll.Notifications;

/// <summary>What a provider accepted: its message id, and how many billable units it was (SMS parts; an email is 1).</summary>
public sealed record Sent(string ProviderId, int Units);

/// <summary>The provider didn't take the message; the message says why, for the retry log and support.</summary>
public sealed class DeliveryException(string message) : Exception(message);

public interface IEmailChannel
{
    Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct);
}

public interface ISmsChannel
{
    Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct);
}

public sealed class PostmarkOptions
{
    public string ServerToken { get; set; } = "";
    public string From { get; set; } = "Pesaroll <no-reply@pesaroll.co.ke>";
    public string MessageStream { get; set; } = "outbound";
}

/// <summary>Email through Postmark's API (https://postmarkapp.com/developer/api/email-api).</summary>
public sealed class PostmarkEmail(HttpClient http, PostmarkOptions options) : IEmailChannel
{
    private static readonly JsonSerializerOptions PascalCase = new() { PropertyNamingPolicy = null };

    public async Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.postmarkapp.com/email")
        {
            Content = JsonContent.Create(new
            {
                From = options.From, To = to, message.Subject, TextBody = message.Text, HtmlBody = message.Html, options.MessageStream,
            }, options: PascalCase),   // Postmark's field names are PascalCase
        };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Postmark-Server-Token", options.ServerToken);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = TryParse(body);
        var code = json is { } j && j.TryGetProperty("ErrorCode", out var c) ? c.GetInt32() : -1;
        if (!response.IsSuccessStatusCode || code != 0)
        {
            var reason = json is { } e && e.TryGetProperty("Message", out var m) ? m.GetString() : body;
            throw new DeliveryException($"Postmark {code}: {reason}");
        }

        return new Sent(json!.Value.GetProperty("MessageID").GetString()!, 1);
    }

    private static JsonElement? TryParse(string body)
    {
        try { return JsonDocument.Parse(body).RootElement; }
        catch (JsonException) { return null; }
    }
}

public sealed class AfricasTalkingOptions
{
    public string Username { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>The approved sender name (BIZ-10); until then Africa's Talking's shared one is used.</summary>
    public string? SenderId { get; set; }
    /// <summary>The free sandbox: nothing reaches a phone; messages show in the sandbox simulator.</summary>
    public bool Sandbox { get; set; }
}

/// <summary>SMS through Africa's Talking (https://developers.africastalking.com/docs/sms/sending).</summary>
public sealed class AfricasTalkingSms(HttpClient http, AfricasTalkingOptions options) : ISmsChannel
{
    // Processed, Sent and Queued; anything else (blacklisted, invalid number, low balance…) failed.
    private static readonly int[] Accepted = [100, 101, 102];

    public async Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["username"] = options.Username, ["to"] = to, ["message"] = message.Sms };
        if (!options.Sandbox && !string.IsNullOrWhiteSpace(options.SenderId))
        {
            form["from"] = options.SenderId;
        }

        var host = options.Sandbox ? "api.sandbox.africastalking.com" : "api.africastalking.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/version1/messaging") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("apiKey", options.ApiKey);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new DeliveryException($"Africa's Talking {(int)response.StatusCode}: {body}");
        }

        var recipient = JsonDocument.Parse(body).RootElement.GetProperty("SMSMessageData").GetProperty("Recipients")[0];
        var status = recipient.GetProperty("statusCode").GetInt32();
        if (!Accepted.Contains(status))
        {
            throw new DeliveryException($"Africa's Talking {recipient.GetProperty("status").GetString()} ({status})");
        }

        return new Sent(recipient.GetProperty("messageId").GetString()!, SmsParts.Count(message.Sms));
    }
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "Pesaroll <no-reply@pesaroll.co.ke>";
}

/// <summary>Email to a local inbox (Mailpit in compose) during development; never used in production.</summary>
public sealed class SmtpEmail(SmtpOptions options) : IEmailChannel
{
    public async Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct)
    {
        using var mail = new MailMessage { From = new MailAddress(options.From), Subject = message.Subject, Body = message.Text };
        mail.To.Add(to);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Html, null, "text/html"));
        using var smtp = new SmtpClient(options.Host, options.Port);
        try
        {
            await smtp.SendMailAsync(mail, ct);
        }
        catch (SmtpException error)
        {
            throw new DeliveryException($"SMTP: {error.Message}");
        }

        return new Sent($"smtp-{Guid.NewGuid():N}", 1);
    }
}

/// <summary>SMS written to the log (masked) during development, when no sandbox key is set.</summary>
public sealed class LogSms(ILogger<LogSms> logger) : ISmsChannel
{
    public Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct)
    {
        logger.LogInformation("SMS to {To}: {Text}", Recipients.Mask(to), message.Sms);
        return Task.FromResult(new Sent($"log-{Guid.NewGuid():N}", SmsParts.Count(message.Sms)));
    }
}
