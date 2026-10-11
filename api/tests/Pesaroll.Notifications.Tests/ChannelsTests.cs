using System.Net;
using System.Text;
using System.Text.Json;

namespace Pesaroll.Notifications.Tests;

/// <summary>NTF-01: what is sent to Postmark (email) and Africa's Talking (SMS), and what comes back.</summary>
public sealed class ChannelsTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Sent = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly Rendered Message = new("Reminder: run payroll", "Run payroll by Friday.", "<p>Run payroll by Friday.</p>", "Pesaroll: run payroll by Friday.");

    [Fact]
    public async Task Postmark_gets_the_email_with_the_server_token()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"To":"wanjiru@example.com","MessageID":"b7bc2f4a-e38e","ErrorCode":0,"Message":"OK"}""");
        var postmark = new PostmarkEmail(new HttpClient(handler), new PostmarkOptions { ServerToken = "server-token", From = "Pesaroll <no-reply@pesaroll.co.ke>" });

        var sent = await postmark.SendAsync("wanjiru@example.com", Message, CancellationToken.None);

        Assert.Equal(new Sent("b7bc2f4a-e38e", 1), sent);
        Assert.Equal("https://api.postmarkapp.com/email", handler.Request!.RequestUri!.ToString());
        Assert.Equal("server-token", handler.Request.Headers.GetValues("X-Postmark-Server-Token").Single());
        var body = JsonDocument.Parse(handler.Sent!).RootElement;
        Assert.Equal(("Pesaroll <no-reply@pesaroll.co.ke>", "wanjiru@example.com", "Reminder: run payroll", "outbound"),
            (body.GetProperty("From").GetString(), body.GetProperty("To").GetString(), body.GetProperty("Subject").GetString(), body.GetProperty("MessageStream").GetString()));
        Assert.Equal("<p>Run payroll by Friday.</p>", body.GetProperty("HtmlBody").GetString());
    }

    [Fact]
    public async Task A_Postmark_refusal_is_an_error_with_its_reason()
    {
        var handler = new FakeHandler(HttpStatusCode.UnprocessableEntity, """{"ErrorCode":406,"Message":"You tried to send to recipient(s) that have been marked as inactive."}""");
        var postmark = new PostmarkEmail(new HttpClient(handler), new PostmarkOptions { ServerToken = "t", From = "Pesaroll <no-reply@pesaroll.co.ke>" });

        var error = await Assert.ThrowsAsync<DeliveryException>(() => postmark.SendAsync("gone@example.com", Message, CancellationToken.None));

        Assert.Contains("406", error.Message);
        Assert.Contains("inactive", error.Message);
    }

    [Fact]
    public async Task Africas_Talking_gets_the_sms_and_reports_the_parts_billed()
    {
        var handler = new FakeHandler(HttpStatusCode.Created, """
            {"SMSMessageData":{"Message":"Sent to 1/1 Total Cost: KES 0.8000","Recipients":[{"statusCode":101,"number":"+254712345678","status":"Success","cost":"KES 0.8000","messageId":"ATXid_1"}]}}
            """);
        var at = new AfricasTalkingSms(new HttpClient(handler), new AfricasTalkingOptions { Username = "pesaroll", ApiKey = "at-key", SenderId = "PESAROLL" });

        var sent = await at.SendAsync("+254712345678", Message, CancellationToken.None);

        Assert.Equal(new Sent("ATXid_1", 1), sent);
        Assert.Equal("https://api.africastalking.com/version1/messaging", handler.Request!.RequestUri!.ToString());
        Assert.Equal("at-key", handler.Request.Headers.GetValues("apiKey").Single());
        var form = handler.Sent!.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        Assert.Equal(("pesaroll", "+254712345678", "Pesaroll: run payroll by Friday.", "PESAROLL"), (form["username"], form["to"], form["message"], form["from"]));
    }

    [Fact]
    public async Task The_sandbox_has_its_own_address_and_no_sender_id()
    {
        var handler = new FakeHandler(HttpStatusCode.Created, """{"SMSMessageData":{"Recipients":[{"statusCode":101,"status":"Success","messageId":"ATXid_2"}]}}""");
        var at = new AfricasTalkingSms(new HttpClient(handler), new AfricasTalkingOptions { Username = "sandbox", ApiKey = "k", SenderId = "PESAROLL", Sandbox = true });

        await at.SendAsync("+254712345678", Message, CancellationToken.None);

        Assert.Equal("https://api.sandbox.africastalking.com/version1/messaging", handler.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("from=", handler.Sent);
    }

    [Fact]
    public async Task An_sms_Africas_Talking_rejects_is_an_error_with_its_status()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"SMSMessageData":{"Recipients":[{"statusCode":406,"status":"UserInBlacklist","number":"+254712345678"}]}}""");
        var at = new AfricasTalkingSms(new HttpClient(handler), new AfricasTalkingOptions { Username = "u", ApiKey = "k" });

        var error = await Assert.ThrowsAsync<DeliveryException>(() => at.SendAsync("+254712345678", Message, CancellationToken.None));

        Assert.Contains("UserInBlacklist", error.Message);
    }
}
