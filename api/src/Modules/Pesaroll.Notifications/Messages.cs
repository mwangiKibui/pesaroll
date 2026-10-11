using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Pesaroll.Notifications;

/// <summary>Who a message goes to: checked, stored one way, and masked wherever it is logged.</summary>
public static partial class Recipients
{
    /// <summary>A Kenyan mobile number (07…, 01…, 254…, +254…) as +254XXXXXXXXX, or null.</summary>
    public static string? KenyanMobile(string? typed)
    {
        var compact = Separators().Replace(typed ?? "", "");
        var match = KenyanMobilePattern().Match(compact);
        return match.Success ? "+254" + match.Groups[1].Value : null;
    }

    /// <summary>A plain email address, trimmed and lower case, or null.</summary>
    public static string? Email(string? typed)
    {
        var email = (typed ?? "").Trim().ToLowerInvariant();
        return email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && parsed.Host.Contains('.')
            ? email : null;
    }

    /// <summary>"+254 7•• ••• 678" or "w•••@example.com": enough to tell recipients apart, not to reach them.</summary>
    public static string Mask(string recipient)
    {
        if (recipient.StartsWith("+254", StringComparison.Ordinal) && recipient.Length == 13)
        {
            return $"+254 {recipient[4]}•• ••• {recipient[^3..]}";
        }

        var at = recipient.IndexOf('@');
        return at > 0 ? $"{recipient[0]}•••{recipient[at..]}" : "•••";
    }

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^(?:\+?254|0)?([17]\d{8})$")]
    private static partial Regex KenyanMobilePattern();
}

/// <summary>
/// How many SMS parts a text is billed as. Texts in the GSM alphabet fit 160 characters in one
/// SMS and 153 per part when longer (a few characters such as [ ] { } ~ count twice); any other
/// character (an emoji, most non-Latin letters) makes it UCS-2: 70, then 67 per part.
/// </summary>
public static class SmsParts
{
    private const string Basic = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
    private const string Extended = "^{}\\[~]|€\f";

    public static int Count(string text)
    {
        if (text.All(c => Basic.Contains(c) || Extended.Contains(c)))
        {
            var length = text.Sum(c => Extended.Contains(c) ? 2 : 1);
            return length <= 160 ? 1 : (length + 152) / 153;
        }

        return text.Length <= 70 ? 1 : (text.Length + 66) / 67;   // UTF-16 code units, as carriers count UCS-2
    }
}

/// <summary>A message ready to send: the email's subject, text and HTML, and the SMS text.</summary>
public sealed record Rendered(string Subject, string Text, string Html, string Sms);

/// <summary>
/// The messages Pesaroll sends, in English and Swahili. Values fill {placeholders}; in HTML they
/// are escaped. Never put salaries or other pay figures in a template: email and SMS leave Pesaroll.
/// </summary>
public static partial class Templates
{
    private sealed record Template(string Subject, string Text, string Sms);

    private static readonly Dictionary<(string Key, string Language), Template> All = new()
    {
        [("payroll_reminder", "en")] = new(
            "Reminder: run payroll for {schedule} by {payDate}",
            "This is your reminder to run payroll for {schedule} at {organization}. Pay day is {payDate}.\n\nOpen Pesaroll to run it: {link}\n\nYou get this because a reminder was set on the {schedule} payroll schedule.",
            "Pesaroll: reminder to run payroll for {schedule} ({organization}). Pay day is {payDate}. {link}"),
        [("payroll_reminder", "sw")] = new(
            "Kumbusho: endesha mishahara ya {schedule} kabla ya {payDate}",
            "Hili ni kumbusho la kuendesha mishahara ya {schedule} katika {organization}. Siku ya malipo ni {payDate}.\n\nFungua Pesaroll uiendeshe: {link}\n\nUmepokea ujumbe huu kwa sababu kumbusho liliwekwa kwenye ratiba ya mishahara ya {schedule}.",
            "Pesaroll: kumbusho la kuendesha mishahara ya {schedule} ({organization}). Siku ya malipo ni {payDate}. {link}"),
    };

    public static bool Exists(string key) => All.ContainsKey((key, "en"));

    public static Rendered Render(string key, string language, IReadOnlyDictionary<string, string> data)
    {
        var template = All.GetValueOrDefault((key, language)) ?? All.GetValueOrDefault((key, "en"))
            ?? throw new ArgumentException($"No template {key}.", nameof(key));
        string Fill(string text, Func<string, string> encode) => Placeholder().Replace(text, m =>
            data.TryGetValue(m.Groups[1].Value, out var value) ? encode(value) : throw new ArgumentException($"Template {key} needs {m.Groups[1].Value}."));

        var html = string.Join("", Fill(template.Text, WebUtility.HtmlEncode).Split("\n\n").Select(p => $"<p>{p}</p>"));
        return new Rendered(Fill(template.Subject, v => v), Fill(template.Text, v => v), html, Fill(template.Sms, v => v));
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
