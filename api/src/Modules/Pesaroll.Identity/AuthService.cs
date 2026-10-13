using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Identity;

public sealed record SignUpRequest(
    string? FirstName, string? MiddleName, string? LastName, string? Email, string? Phone, string? Password, Guid? RoleId, string? RoleOther);

public sealed record SignInRequest(string? Identifier, string? Password);

/// <summary>
/// An invitation as the accept page shows it: the organization, who invited them, their name and
/// email or phone as invited, the groups (one or more of administrators, hr, payroll, approvers) and companies,
/// and whether they sign in to accept (an account with that email or phone exists).
/// </summary>
public sealed record InvitationResponse(string Organization, string InvitedBy, string FirstName, string LastName, string? Email, string? Phone,
    IReadOnlyList<string> Groups, bool AllCompanies, IReadOnlyList<string> Companies, DateTimeOffset ExpiresAt, bool HasAccount);

/// <summary>Accepting: their name and a new password, or, with an account already, its password.</summary>
public sealed record AcceptInvitationRequest(string? FirstName, string? LastName, string? Password);

/// <param name="CanOnboardOrganization">
/// True for someone who signed up themselves and hasn't onboarded an organization yet: the app takes
/// them to the organization step. Anyone else without an organization sees "not part of an organization".
/// </param>
/// <param name="Permissions">What they may do in their organization, each for all companies or some (ACC-02).</param>
public sealed record MeResponse(
    Guid Id, string FirstName, string? MiddleName, string LastName, string FullName, string? Email, string? Phone,
    Guid? SignupRoleId, string? SignupRoleOther, Guid? OrganizationId, string SignedUpVia, bool CanOnboardOrganization,
    IReadOnlyList<AccessGrant> Permissions)
{
    public static MeResponse From(User user, Guid? organizationId, bool hasOnboardedOrganization, IReadOnlyList<AccessGrant> permissions) =>
        new(user.Id, user.FirstName, user.MiddleName, user.LastName, user.FullName, user.Email, user.Phone,
            user.SignupRoleId, user.SignupRoleOther, organizationId, user.SignedUpVia,
            user.SignedUpVia == "self" && !hasOnboardedOrganization, permissions);
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
        var userId = await VerifyAsync(identifier, request.Password ?? "", ct);

        tenant.SetUser(userId);
        var organizationId = await db.Memberships
            .Where(m => m.UserId == userId && m.Status == "active")
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefaultAsync(ct);

        return await sessions.StartAsync(userId, organizationId, http, ct);
    }

    /// <summary>
    /// An invitation, by its link (AUTH-08), for the accept page: who invited them, to what, and
    /// whether they already have an account. Links that no longer work say why.
    /// </summary>
    public async Task<InvitationResponse> InvitationAsync(string token, CancellationToken ct)
    {
        var invitation = await FindInvitationAsync(token, ct);
        return new InvitationResponse(invitation.Organization, invitation.InvitedBy, invitation.FirstName, invitation.LastName, invitation.Email,
            invitation.Phone, invitation.GroupKeys, invitation.AllCompanies, invitation.Companies, invitation.ExpiresAt, invitation.HasAccount);
    }

    /// <summary>
    /// Accepts an invitation: a new account with the invitation's email or phone (the link proves
    /// it), or the existing account's password; then they join the organization in the invitation's
    /// group and are signed in to it.
    /// </summary>
    public async Task<TokenResponse> AcceptInvitationAsync(string token, AcceptInvitationRequest request, HttpContext http, CancellationToken ct)
    {
        var invitation = await FindInvitationAsync(token, ct);
        Guid userId;
        User? user = null;
        if (invitation.HasAccount)
        {
            userId = await VerifyAsync(invitation.Email ?? invitation.Phone!, request.Password ?? "", ct);
        }
        else
        {
            var firstName = request.FirstName?.Trim();
            var lastName = request.LastName?.Trim();
            if (!IsNamePart(firstName) || !IsNamePart(lastName))
            {
                throw AuthErrors.MissingName();
            }

            if (request.Password is null || request.Password.Length < Passwords.MinimumLength)
            {
                throw AuthErrors.PasswordTooShort();
            }

            var now = clock.GetUtcNow();
            user = new User
            {
                Id = Guid.CreateVersion7(), FirstName = firstName!, LastName = lastName!, Email = invitation.Email, Phone = invitation.Phone,
                SignedUpVia = "invitation",
                // The link reached them there.
                EmailVerifiedAt = invitation.Email is null ? null : now, PhoneVerifiedAt = invitation.Phone is null ? null : now,
            };
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            userId = user.Id;
        }

        // The connection takes the person when it opens, so they are set before the transaction starts.
        tenant.SetUser(userId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (user is not null)
        {
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
        }

        Guid organizationId;
        try
        {
            organizationId = (await db.QueryAsync("select accept_invitation(@hash)", r => r.GetGuid(0),
                new Dictionary<string, object?> { ["hash"] = invitation.Hash }, ct)).Single();
        }
        catch (PostgresException error) when (InvitationError(error.MessageText) is { } known)
        {
            throw known;
        }

        await transaction.CommitAsync(ct);
        return await sessions.StartAsync(userId, organizationId, http, ct);
    }

    private sealed record FoundInvitation(byte[] Hash, string Organization, string InvitedBy, string FirstName, string LastName, string? Email, string? Phone,
        string[] GroupKeys, bool AllCompanies, string[] Companies, DateTimeOffset ExpiresAt, bool HasAccount);

    private async Task<FoundInvitation> FindInvitationAsync(string token, CancellationToken ct)
    {
        byte[] raw;
        try
        {
            raw = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            throw AuthErrors.InvitationNotFound();
        }

        var hash = SHA256.HashData(raw);
        var found = (await db.QueryAsync(
            "select organization_name, invited_by_name, first_name, last_name, email, phone, group_keys, all_companies, company_names, status, expires_at, has_account " +
            "from invitation_by_token(@hash)",
            r => (Invitation: new FoundInvitation(hash, r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetFieldValue<string[]>(6), r.GetBoolean(7), r.GetFieldValue<string[]>(8), r.GetFieldValue<DateTimeOffset>(10), r.GetBoolean(11)),
                Status: r.GetString(9)),
            new Dictionary<string, object?> { ["hash"] = hash }, ct)).FirstOrDefault();
        return found.Status switch
        {
            null => throw AuthErrors.InvitationNotFound(),
            "expired" => throw AuthErrors.InvitationExpired(),
            "accepted" => throw AuthErrors.InvitationUsed(),
            "revoked" => throw AuthErrors.InvitationRevoked(),
            _ => found.Invitation,
        };
    }

    private static AppException? InvitationError(string code) => code switch
    {
        "AUTH-1011" => AuthErrors.InvitationNotFound(),
        "AUTH-1012" => AuthErrors.InvitationExpired(),
        "AUTH-1013" => AuthErrors.InvitationUsed(),
        "AUTH-1014" => AuthErrors.InvitationRevoked(),
        "AUTH-1015" => AuthErrors.InvitationForSomeoneElse(),
        _ => null,
    };

    /// <summary>The account's id if the password is right; wrong ones count towards the lockout.</summary>
    private async Task<Guid> VerifyAsync(string identifier, string password, CancellationToken ct)
    {
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
        return account.Id;
    }

    private async Task<DateTimeOffset?> RecordSignInAsync(Guid userId, bool success, CancellationToken ct) =>
        (await db.QueryAsync(
            "select record_signin_result(@user_id, @success)",
            r => r.IsDBNull(0) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(0),
            new Dictionary<string, object?> { ["user_id"] = userId, ["success"] = success }, ct)).Single();

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsNamePart(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 60;
}
