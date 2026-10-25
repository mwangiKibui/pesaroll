using System.Text.Json.Nodes;

namespace Pesaroll.Payroll.Engine.Tests;

/// <summary>PAY-01: a rule pack is read into a typed model, and a malformed one is refused rather than half used.</summary>
public sealed class RulePackTests
{
    [Fact]
    public void Kenya_2026_packs_read()
    {
        var pack = Packs.Year4;

        Assert.Equal("KE-2026.02", pack.Version);
        Assert.Equal("KE", pack.Country);
        Assert.Equal(9_000m, pack.Nssf.LowerLimit);
        Assert.Equal(108_000m, pack.Nssf.UpperLimit);
        Assert.Equal(0.0275m, pack.Shif.Rate);
        Assert.Equal(72_000m, Packs.Year3.Nssf.UpperLimit);
    }

    public static TheoryData<string, Action<JsonObject>> Malformed => new()
    {
        { "no NSSF", p => p.Remove("nssf") },
        { "weekly", p => p["period"] = "weekly" },
        { "bands out of order", p => p["paye"]!["bands"]![1]!["upTo"] = 20_000 },
        { "top band capped", p => p["paye"]!["bands"]![4]!["upTo"] = 900_000 },
        { "rate over 100%", p => p["shif"]!["rate"] = 2.75 },
        { "limits the wrong way round", p => p["nssf"]!["lowerLimit"] = 200_000 },
        { "negative cap", p => p["deductions"]!["pensionCap"] = -1 },
        { "unknown rounding", p => p["rounding"]!["mode"] = "up" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Malformed_pack_is_refused(string why, Action<JsonObject> breakIt)
    {
        var json = JsonNode.Parse(Packs.Json("KE-2026.02"))!.AsObject();
        breakIt(json);

        var error = Assert.Throws<InvalidDataException>(() => RulePack.Parse(json.ToJsonString()));
        Assert.True(error.Message.Contains("KE-2026.02"), $"{why}: {error.Message}");
    }
}
