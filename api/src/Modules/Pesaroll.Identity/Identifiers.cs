using System.Net.Mail;

namespace Pesaroll.Identity;

/// <summary>Normalises the email or phone people type, so the same person always matches.</summary>
public static class Identifiers
{
    /// <summary>Lowercase email, or null when it isn't a valid address.</summary>
    public static string? NormalizeEmail(string? input)
    {
        var email = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Length > 254)
        {
            return null;
        }

        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && email.Contains('.')
            ? email
            : null;
    }

    /// <summary>
    /// A Kenyan mobile number as +254 followed by 9 digits (07…, 01…, 254…, +254… all accepted),
    /// or null when it isn't one. Other countries come with their rule packs.
    /// </summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        var local = digits switch
        {
            { Length: 10 } when digits[0] == '0' => digits[1..],
            { Length: 12 } when digits.StartsWith("254", StringComparison.Ordinal) => digits[3..],
            { Length: 9 } => digits,
            _ => null,
        };

        return local is not null && (local[0] == '7' || local[0] == '1') ? "+254" + local : null;
    }

    /// <summary>An email or phone for sign-in, normalised; null when it is neither.</summary>
    public static string? NormalizeEither(string? input) =>
        input?.Contains('@') == true ? NormalizeEmail(input) : NormalizePhone(input);
}
