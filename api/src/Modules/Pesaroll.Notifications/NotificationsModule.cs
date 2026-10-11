using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Notifications;

public static class NotificationsModule
{
    /// <summary>
    /// Registers queuing (<see cref="Notifier"/>), sending (<see cref="NotificationJob"/>) and the
    /// channels from configuration:
    /// <code>
    /// Notifications:Email:Provider  postmark | smtp (Mailpit locally)
    /// Notifications:Email:Postmark  ServerToken, From
    /// Notifications:Email:Smtp      Host, Port, From
    /// Notifications:Sms:Provider    africastalking | log
    /// Notifications:Sms:AfricasTalking  Username, ApiKey, SenderId, Sandbox
    /// </code>
    /// Production refuses to start with anything but Postmark and Africa's Talking, with their keys.
    /// </summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString, IConfiguration configuration, bool isProduction)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<NotificationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()));
        services.AddScoped<Notifier>();
        services.AddScoped<NotificationJob>();
        services.AddHttpClient();

        var email = configuration.GetSection("Notifications:Email");
        switch (email["Provider"] ?? "smtp")
        {
            case "postmark":
                var postmark = email.GetSection("Postmark").Get<PostmarkOptions>() ?? new PostmarkOptions();
                Require(!string.IsNullOrWhiteSpace(postmark.ServerToken), "Notifications:Email:Postmark:ServerToken is not set.");
                services.AddSingleton<IEmailChannel>(p => new PostmarkEmail(p.GetRequiredService<IHttpClientFactory>().CreateClient("postmark"), postmark));
                break;
            case "smtp":
                Require(!isProduction, "Production sends email through Postmark: set Notifications:Email:Provider to postmark.");
                var smtp = email.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
                services.AddSingleton<IEmailChannel>(new SmtpEmail(smtp));
                break;
            default:
                throw new InvalidOperationException("Notifications:Email:Provider must be postmark or smtp.");
        }

        var sms = configuration.GetSection("Notifications:Sms");
        switch (sms["Provider"] ?? "log")
        {
            case "africastalking":
                var at = sms.GetSection("AfricasTalking").Get<AfricasTalkingOptions>() ?? new AfricasTalkingOptions();
                Require(at.Username.Length > 0 && at.ApiKey.Length > 0, "Notifications:Sms:AfricasTalking needs a Username and an ApiKey.");
                Require(!isProduction || !at.Sandbox, "Production can't send SMS through the Africa's Talking sandbox.");
                services.AddSingleton<ISmsChannel>(p => new AfricasTalkingSms(p.GetRequiredService<IHttpClientFactory>().CreateClient("africastalking"), at));
                break;
            case "log":
                Require(!isProduction, "Production sends SMS through Africa's Talking: set Notifications:Sms:Provider to africastalking.");
                services.AddSingleton<ISmsChannel>(p => new LogSms(p.GetRequiredService<ILogger<LogSms>>()));
                break;
            default:
                throw new InvalidOperationException("Notifications:Sms:Provider must be africastalking or log.");
        }

        return services;
    }

    private static void Require(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException(message);
        }
    }
}
