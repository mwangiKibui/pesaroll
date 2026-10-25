using System.Linq.Expressions;
using Pesaroll.Identity;

namespace Pesaroll.Organizations;

/// <summary>
/// What payroll needs from each employee (PAY-03 enforces it), as one list of rules: the Employees
/// readiness checks a loaded person against them, and Home (OP-30) counts them in the database, so
/// the two always agree, and the Employees list filters by them (OP-07). An item's code is its
/// catalogue error when the pay run has one (PAY-2001).
/// </summary>
public static class EmployeeReadiness
{
    public sealed record Rule(MissingItem Item, Expression<Func<Employee, bool>> IsMissing)
    {
        private readonly Func<Employee, bool> check = IsMissing.Compile();

        public string? Code => Item.Code;

        public bool MissingFor(Employee e) => check(e);
    }

    public static readonly IReadOnlyList<Rule> Rules =
    [
        new(new MissingItem("employee", "kra_pin", new LocalizedText("KRA PIN", "PIN ya KRA"), "PAY-2001"), e => e.KraPinEnc == null),
        new(new MissingItem("employee", "national_id", new LocalizedText("ID number", "Nambari ya kitambulisho")), e => e.NationalIdEnc == null),
        new(new MissingItem("employee", "basic_pay", new LocalizedText("Basic pay", "Mshahara wa msingi")),
            e => !e.Compensations.Any(c => c.ValidTo == null)),
        new(new MissingItem("employee", "payment_account", new LocalizedText("Where to pay them", "Mahali pa kuwalipa")),
            e => !e.Accounts.Any(a => a.ValidTo == null)),
    ];

    /// <summary>What this person still needs (their current pay and account loaded).</summary>
    public static ReadinessResponse For(Employee e)
    {
        var missing = Rules.Where(r => r.MissingFor(e)).Select(r => r.Item).ToList();
        return new ReadinessResponse(missing.Count == 0, missing);
    }

    /// <summary>People with nothing missing, for a database count.</summary>
    public static Expression<Func<Employee, bool>> Ready { get; } = AllPresent();

    /// <summary>People missing anything, for a database filter.</summary>
    public static Expression<Func<Employee, bool>> NotReady { get; } =
        Expression.Lambda<Func<Employee, bool>>(Expression.Not(Ready.Body), Ready.Parameters);

    private static Expression<Func<Employee, bool>> AllPresent()
    {
        var person = Expression.Parameter(typeof(Employee), "e");
        Expression body = Expression.Constant(true);
        foreach (var rule in Rules)
        {
            var missing = new Swap(rule.IsMissing.Parameters[0], person).Visit(rule.IsMissing.Body);
            body = Expression.AndAlso(body, Expression.Not(missing));
        }

        return Expression.Lambda<Func<Employee, bool>>(body, person);
    }

    private sealed class Swap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
