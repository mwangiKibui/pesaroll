namespace Pesaroll.Payroll.Engine.Tests;

/// <summary>The rule packs from database/rule-packs/, the same files post-deploy publishes.</summary>
internal static class Packs
{
    public static RulePack Load(string version) =>
        RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", $"{version}.json")));

    public static string Json(string version) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", $"{version}.json"));

    /// <summary>NSSF Year 3: pay dates to 31 January 2026.</summary>
    public static RulePack Year3 => Load("KE-2025.02");

    /// <summary>NSSF Year 4: pay dates from 1 February 2026.</summary>
    public static RulePack Year4 => Load("KE-2026.02");
}
