using System.Text.RegularExpressions;

namespace Pesaroll.Organizations;

/// <summary>
/// An employee's statutory numbers (ONB-09), checked and stored one way. Null means it doesn't look
/// right. What is known of each format (docs/research, 2026-10):
///   KRA PIN      a person's: A, 9 digits, a letter (KRA)
///   ID number    7 or 8 digits (older and current national IDs), or a 9-digit Maisha Namba
///   NSSF number  digits, usually 9; 7 to 10 accepted (not published by NSSF)
///   SHA number   not published; letters, digits and dashes, 6 to 20 (SHA also takes the ID number)
///   M-Pesa       a Safaricom line: 070-072, 0740-0743, 0745, 0746, 0748, 0757-0759, 0768, 0769,
///                079, 0110-0115, and the 2026 ranges 0118, 0119, 0140-0143, 0180-0182
/// </summary>
public static partial class EmployeeNumbers
{
    public static string? KraPin(string? typed) => Matching(typed, PersonPin(), upper: true);

    public static string? NationalId(string? typed) => Matching(typed, IdNumber(), upper: false);

    public static string? Nssf(string? typed) => Matching(typed, NssfNumber(), upper: false);

    public static string? Sha(string? typed) => Matching(typed, ShaNumber(), upper: true);

    /// <summary>A Safaricom mobile number (M-Pesa) as +254XXXXXXXXX, or null.</summary>
    public static string? Mpesa(string? typed)
    {
        var number = Pesaroll.Notifications.Recipients.KenyanMobile(typed);
        return number is not null && SafaricomPrefix(int.Parse(number[4..7])) ? number : null;
    }

    /// <summary>The three digits after 0 (or 254): 712 for 0712…, 110 for 0110….</summary>
    private static bool SafaricomPrefix(int p) =>
        p is (>= 700 and <= 729) or (>= 740 and <= 743) or 745 or 746 or 748 or (>= 757 and <= 759) or 768 or 769 or (>= 790 and <= 799)
          or (>= 110 and <= 115) or 118 or 119 or (>= 140 and <= 143) or (>= 180 and <= 182);

    /// <summary>"•••• 5678": the last four, for screens.</summary>
    public static string Mask(string value) => value.Length <= 4 ? "••••" : $"•••• {value[^4..]}";

    private static string? Matching(string? typed, Regex pattern, bool upper)
    {
        var compact = Regex.Replace(typed ?? "", @"\s", "");
        compact = upper ? compact.ToUpperInvariant() : compact;
        return pattern.IsMatch(compact) ? compact : null;
    }

    [GeneratedRegex("^A[0-9]{9}[A-Z]$")]
    private static partial Regex PersonPin();

    [GeneratedRegex("^[0-9]{7,9}$")]
    private static partial Regex IdNumber();

    [GeneratedRegex("^[0-9]{7,10}$")]
    private static partial Regex NssfNumber();

    [GeneratedRegex("^[A-Z0-9-]{6,20}$")]
    private static partial Regex ShaNumber();
}
