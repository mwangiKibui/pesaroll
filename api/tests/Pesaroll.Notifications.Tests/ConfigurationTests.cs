using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pesaroll.Notifications.Tests;

/// <summary>NTF-01: local runs use Mailpit and the log; production only ever Postmark and Africa's Talking.</summary>
public sealed class ConfigurationTests
{
    private static IServiceCollection Add(bool production, params (string Key, string Value)[] settings) =>
        new ServiceCollection().AddLogging().AddNotificationsModule("Host=unused",
            new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value))).Build(), production);

    [Fact]
    public void Locally_email_goes_to_Mailpit_and_sms_to_the_log()
    {
        var services = Add(production: false).BuildServiceProvider();

        Assert.IsType<SmtpEmail>(services.GetRequiredService<IEmailChannel>());
        Assert.IsType<LogSms>(services.GetRequiredService<ISmsChannel>());
    }

    [Fact]
    public void Production_refuses_to_start_without_the_real_providers()
    {
        Assert.Contains("Postmark", Assert.Throws<InvalidOperationException>(() => Add(production: true)).Message);
        Assert.Contains("Africa's Talking", Assert.Throws<InvalidOperationException>(() => Add(production: true,
            ("Notifications:Email:Provider", "postmark"), ("Notifications:Email:Postmark:ServerToken", "t"))).Message);
        Assert.Contains("sandbox", Assert.Throws<InvalidOperationException>(() => Add(production: true,
            ("Notifications:Email:Provider", "postmark"), ("Notifications:Email:Postmark:ServerToken", "t"),
            ("Notifications:Sms:Provider", "africastalking"), ("Notifications:Sms:AfricasTalking:Username", "pesaroll"),
            ("Notifications:Sms:AfricasTalking:ApiKey", "k"), ("Notifications:Sms:AfricasTalking:Sandbox", "true"))).Message);
    }

    [Fact]
    public void Production_sends_through_Postmark_and_Africas_Talking()
    {
        var services = Add(production: true,
            ("Notifications:Email:Provider", "postmark"), ("Notifications:Email:Postmark:ServerToken", "t"),
            ("Notifications:Sms:Provider", "africastalking"), ("Notifications:Sms:AfricasTalking:Username", "pesaroll"),
            ("Notifications:Sms:AfricasTalking:ApiKey", "k")).BuildServiceProvider();

        Assert.IsType<PostmarkEmail>(services.GetRequiredService<IEmailChannel>());
        Assert.IsType<AfricasTalkingSms>(services.GetRequiredService<ISmsChannel>());
    }
}
