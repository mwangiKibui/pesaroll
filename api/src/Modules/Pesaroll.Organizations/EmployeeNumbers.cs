using System.Text.RegularExpressions;

namespace Pesaroll.Organizations;

/// <summary>
/// An employee's statutory numbers (ONB-09), checked and stored one way: a person's KRA PIN
/// (A, 9 digits, a letter), a national ID number (7 or 8 digits), NSSF and SHA numbers. Null means
/// it doesn't look right.
/// </summary>
public static partial class EmployeeNumbers
{
    public static string? KraPin(string? typed) => Matching(typed, PersonPin(), upper: true);

    public static string? NationalId(string? typed) => Matching(typed, IdNumber(), upper: false);

    public static string? Nssf(string? typed) => Matching(typed, NssfNumber(), upper: false);

    public static string? Sha(string? typed) => Matching(typed, ShaNumber(), upper: true);

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

    [GeneratedRegex("^[0-9]{7,8}$")]
    private static partial Regex IdNumber();

    [GeneratedRegex("^[0-9]{6,12}$")]
    private static partial Regex NssfNumber();

    [GeneratedRegex("^[A-Z0-9-]{6,20}$")]
    private static partial Regex ShaNumber();
}
