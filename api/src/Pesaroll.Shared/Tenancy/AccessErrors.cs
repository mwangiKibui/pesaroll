using Pesaroll.Shared.Errors;

namespace Pesaroll.Shared.Tenancy;

/// <summary>Access errors every module uses (ACC, docs/standards/errors.md).</summary>
public static class AccessErrors
{
    public static AppException NoPermission() =>
        new("ACC-8001", "You don't have permission to do this. Ask an administrator in your organization.", 403);
}
