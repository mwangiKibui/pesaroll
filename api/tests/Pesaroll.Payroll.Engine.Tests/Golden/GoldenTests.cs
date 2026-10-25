using System.Text.Json;

namespace Pesaroll.Payroll.Engine.Tests.Golden;

/// <summary>
/// PAY-02: the golden suite. Worked examples in Golden/cases/, one file each: a month's pay, its pay
/// date and rule pack, and every statutory line expected to the cent. Each runs through the engine
/// with the packs post-deploy publishes, and any difference fails the build naming the case and the
/// line. A case's "checked" says who checked it: "computed from sources" until the payroll accountant
/// (BIZ-08) reviews it. How to add one: Golden/README.md.
/// </summary>
public sealed class GoldenTests
{
    private static readonly string Cases = Path.Combine(AppContext.BaseDirectory, "Golden", "cases");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private sealed record Checked(string By, DateOnly On);

    private sealed record Case(
        string Description, DateOnly PayDate, string RulePack, PayInputs Inputs,
        Dictionary<string, decimal> Expected, Checked Checked);

    public static TheoryData<string> All() =>
        new(Directory.GetFiles(Cases, "*.json").Select(Path.GetFileNameWithoutExtension).Order()!);

    [Fact]
    public void The_suite_has_its_cases() => Assert.True(All().Count >= 20, $"only {All().Count} golden cases in {Cases}");

    [Theory]
    [MemberData(nameof(All))]
    public void Case_matches_to_the_cent(string name)
    {
        var golden = JsonSerializer.Deserialize<Case>(File.ReadAllText(Path.Combine(Cases, $"{name}.json")), Json)!;
        var pay = PayCalculator.Calculate(golden.Inputs, Packs.Load(golden.RulePack));

        var actual = pay.Deductions.Concat(pay.EmployerCosts).ToDictionary(l => l.Key, l => l.Amount);
        actual["taxablePay"] = pay.TaxablePay;
        actual["netPay"] = pay.NetPay;

        var unknown = golden.Expected.Keys.Except(actual.Keys).ToList();
        var unchecked_ = actual.Keys.Except(golden.Expected.Keys).ToList();
        var wrong = golden.Expected.Keys.Intersect(actual.Keys)
            .Where(key => golden.Expected[key] != actual[key])
            .Select(key => $"{key}: expected {golden.Expected[key]:N2}, got {actual[key]:N2}")
            .ToList();
        var problems = wrong
            .Concat(unknown.Select(key => $"{key}: the engine has no such line"))
            .Concat(unchecked_.Select(key => $"{key}: not in the case; every line must be expected"))
            .ToList();

        Assert.True(problems.Count == 0, $"Golden case {name} ({golden.Description}):\n  " + string.Join("\n  ", problems));
    }
}
