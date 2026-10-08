using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Identity;

public sealed record SignUpRequest(
    string? FirstName, string? MiddleName, string? LastName, string? Email, string? Phone, string? Password, Guid? RoleId, string? RoleOther);

public sealed record SignInRequest(string? Identifier, string? Password);

/// <param name="CanOnboardOrganization">
/// True for someone who signed up themselves and hasn't onboarded an organization yet: the app takes
/// them to the organization step. Anyone else without an organization sees "not part of an organization".
/// </param>
public sealed record MeResponse(
    Guid Id, string FirstName, string? MiddleName, string LastName, string FullName, string? Email, string? Phone,
    Guid? SignupRoleId, string? SignupRoleOther, Guid? OrganizationId, string SignedUpVia, bool CanOnboardOrganization)
{
    public static MeResponse From(User user, Guid? organizationId, bool hasOnboardedOrganization) =>
        new(user.Id, user.FirstName, user.MiddleName, user.LastName, user.FullName, user.Email, user.Phone,
            user.SignupRoleId, user.SignupRoleOther, organizationId, user.SignedUpVia,
            user.SignedUpVia == "self" && !hasOnboardedOrganization);
}

/// <summary>Sign-up and sign-in (AUTH-01, AUTH-02).</summary>
public sealed class AuthService(
    IdentityDbContext db, TenantContext tenant, SessionService sessions, IPasswordHasher<User> hasher, TimeProvider clock)
{
    private static readonly User TimingUser = new() { FirstName = "timing", LastName = "timing" };
    private static readonly string TimingHash = new PasswordHasher<User>().HashPassword(TimingUser, "not-a-real-password");

    public async Task<TokenResponse> SignUpAsync(SignUpRequest request, HttpContext http, CancellationToken ct)
    {
        var firstName = request.FirstName?.Trim();
        var middleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim();
        var lastName = request.LastName?.Trim();
        var role = request.RoleId is Guid roleId
            ? await db.SignupRoles.SingleOrDefaultAsync(r => r.Id == roleId && r.Active, ct)
            : null;
        if (!IsNamePart(firstName) || !IsNamePart(lastName) || (middleName is not null && !IsNamePart(middleName)) || role is null)
        {
            throw AuthErrors.MissingDetails();
        }

        // "Other" needs the person's own role; any other choice ignores typed text.
        var roleOther = request.RoleOther?.Trim();
        if (role.IsOther && (string.IsNullOrEmpty(roleOther) || roleOther.Length > 80))
        {
            throw AuthErrors.OtherRoleNotTyped();
        }

        var email = Blank(request.Email) ? null : Identifiers.NormalizeEmail(request.Email) ?? throw AuthErrors.InvalidIdentifier();
        var phone = Blank(request.Phone) ? null : Identifiers.NormalizePhone(request.Phone) ?? throw AuthErrors.InvalidIdentifier();
        if (email is null && phone is null)
        {
            throw AuthErrors.InvalidIdentifier();
        }

        if (request.Password is null || request.Password.Length < Passwords.MinimumLength)
        {
            throw AuthErrors.PasswordTooShort();
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            FirstName = firstName!,
            MiddleName = middleName,
            LastName = lastName!,
            Email = email,
            Phone = phone,
            SignupRoleId = role.Id,
            SignupRoleOther = role.IsOther ? roleOther : null,
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);

        // Row-level security only lets a person create their own row.
        tenant.SetUser(user.Id);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            tenant.Clear();
            throw AuthErrors.AlreadyExists();
        }

        return await sessions.StartAsync(user.Id, organizationId: null, http, ct);
    }

    public async Task<TokenResponse> SignInAsync(SignInRequest request, HttpContext http, CancellationToken ct)
    {
        var identifier = Identifiers.NormalizeEither(request.Identifier) ?? throw AuthErrors.InvalidIdentifier();
        var password = request.Password ?? "";

        var account = (await db.QueryAsync(
            "select id, password_hash, status, locked_until from find_account_for_signin(@identifier)",
            r => (Id: r.GetGuid(0), Hash: r.IsDBNull(1) ? null : r.GetString(1), Status: r.GetString(2),
                  LockedUntil: r.IsDBNull(3) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(3)),
            new Dictionary<string, object?> { ["identifier"] = identifier }, ct)).FirstOrDefault();

        if (account == default || account.Hash is null || account.Status != "active")
        {
            // Same work as a real check, so response time doesn't reveal whether the account exists.
            hasher.VerifyHashedPassword(TimingUser, TimingHash, password);
            throw AuthErrors.WrongCredentials();
        }

        if (account.LockedUntil > clock.GetUtcNow())
        {
            throw AuthErrors.Locked();
        }

        var result = hasher.VerifyHashedPassword(TimingUser, account.Hash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            var lockedUntil = await RecordSignInAsync(account.Id, success: false, ct);
            throw lockedUntil > clock.GetUtcNow() ? AuthErrors.Locked() : AuthErrors.WrongCredentials();
        }

        await RecordSignInAsync(account.Id, success: true, ct);

        tenant.SetUser(account.Id);
        var organizationId = await db.Memberships
            .Where(m => m.UserId == account.Id && m.Status == "active")
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefaultAsync(ct);

        return await sessions.StartAsync(account.Id, organizationId, http, ct);
    }

    private async Task<DateTimeOffset?> RecordSignInAsync(Guid userId, bool success, CancellationToken ct) =>
        (await db.QueryAsync(
            "select record_signin_result(@user_id, @success)",
            r => r.IsDBNull(0) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(0),
            new Dictionary<string, object?> { ["user_id"] = userId, ["success"] = success }, ct)).Single();

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsNamePart(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 60;
}
