using System.Text.Json;
using Pesaroll.Shared.Audit;

namespace Pesaroll.Shared.Tests;

/// <summary>
/// PLAT-03: named actions in the audit log. Each is in the catalogue (docs/standards/audit.md) and has
/// its wording in every language; its details are only those its catalogue entry allows, so personal
/// data (an amount, an ID number, an account) can't be put in one.
/// </summary>
public sealed class AuditActionsTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Details_an_action_does_not_allow_are_refused()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            AuditActions.Check(AuditActions.EmployeePayChanged, new Dictionary<string, object?> { ["validFrom"] = "2026-10-01", ["basicAmount"] = 120000m }));
        Assert.Contains("basicAmount", error.Message);
        AuditActions.Check(AuditActions.EmployeePayChanged, new Dictionary<string, object?> { ["validFrom"] = "2026-10-01" });
    }

    [Fact]
    public void Actions_not_in_the_catalogue_are_refused() =>
        Assert.Throws<ArgumentException>(() => AuditActions.Check("pay_run.paid_twice", null));

    [Fact]
    public void Every_action_is_in_the_catalogue_doc_and_worded_in_every_language()
    {
        var doc = File.ReadAllText(Path.Combine(Root, "docs/standards/audit.md"));
        foreach (var locale in new[] { "en", "sw" })
        {
            using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, $"web/src/i18n/locales/{locale}.json")));
            var actions = file.RootElement.GetProperty("audit").GetProperty("actions");
            foreach (var action in AuditActions.All.Keys)
            {
                Assert.True(doc.Contains($"`{action}`"), $"{action} is not in docs/standards/audit.md");
                var (area, name) = (action.Split('.')[0], action.Split('.')[1]);
                Assert.True(actions.TryGetProperty(area, out var words) && words.TryGetProperty(name, out _), $"{action} has no wording in {locale}.json");
            }
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
        }

        throw new InvalidOperationException("The repository root (CLAUDE.md) wasn't found.");
    }
}
