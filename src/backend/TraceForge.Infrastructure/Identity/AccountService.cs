using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TraceForge.Application.Accounts;
using TraceForge.Application.Audit;
using TraceForge.Application.Common;
using TraceForge.Domain.Audit;
using TraceForge.Domain.Identity;
using TraceForge.Infrastructure.Configuration;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Infrastructure.Identity;

internal sealed class AccountService : IAccountService
{
    /// <summary>
    /// Serialises the "first account becomes Administrator" decision inside one process.
    /// Multi-instance deployments must add a database-level guard; see phase-1 report limitations.
    /// </summary>
    private static readonly SemaphoreSlim BootstrapLock = new(1, 1);

    /// <summary>
    /// Pre-computed hash used to spend comparable time when an account does not exist, so that
    /// response timing does not reveal which e-mail addresses are registered.
    /// </summary>
    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "TraceForge-Dummy-Password-1!");

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly TraceForgeDbContext _dbContext;
    private readonly IAuditLogWriter _auditLog;
    private readonly IRequestContext _requestContext;
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        TraceForgeDbContext dbContext,
        IAuditLogWriter auditLog,
        IRequestContext requestContext,
        IOptions<AuthOptions> options,
        TimeProvider timeProvider,
        ILogger<AccountService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _auditLog = auditLog;
        _requestContext = requestContext;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AccountResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationErrors = ValidateRegistration(command);
        if (validationErrors.Count > 0)
        {
            return AccountResult.Failed(AccountFailureReason.InvalidInput, [.. validationErrors]);
        }

        var email = command.Email.Trim();
        var displayName = command.DisplayName.Trim();

        var isFirstAccount = await IsFirstAccountAsync(cancellationToken);

        if (!isFirstAccount && !_options.AllowSelfRegistration)
        {
            await AuditAsync(
                AuditActions.UserRegistered,
                AuditOutcome.Denied,
                cancellationToken,
                actorIdentifier: email,
                details: "Self-registration is disabled.");

            return AccountResult.Failed(
                AccountFailureReason.RegistrationDisabled,
                "Self-registration is disabled on this instance.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
        };

        var createResult = await _userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            var messages = createResult.Errors.Select(e => e.Description).ToArray();
            var failure = createResult.Errors.Any(e =>
                e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
                ? AccountFailureReason.DuplicateAccount
                : AccountFailureReason.WeakPassword;

            await AuditAsync(
                AuditActions.UserRegistered,
                AuditOutcome.Failure,
                cancellationToken,
                actorIdentifier: email,
                details: string.Join("; ", createResult.Errors.Select(e => e.Code)));

            return AccountResult.Failed(failure, messages);
        }

        var role = isFirstAccount ? ApplicationRoles.Administrator : ApplicationRoles.Analyst;

        var roleResult = await AssignRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            // Leave no half-created account behind if role assignment fails.
            await _userManager.DeleteAsync(user);
            _logger.LogError("Role assignment failed for a new account: {Codes}",
                string.Join(",", roleResult.Errors.Select(e => e.Code)));

            return AccountResult.Failed(
                AccountFailureReason.InvalidInput,
                "The account could not be provisioned with a role. Try again or contact an administrator.");
        }

        await AuditAsync(
            AuditActions.UserRegistered,
            AuditOutcome.Success,
            cancellationToken,
            actorUserId: user.Id,
            actorIdentifier: email,
            targetId: user.Id.ToString(),
            details: $"role={role}");

        await _signInManager.SignInAsync(user, isPersistent: false);
        return AccountResult.Success(await ToAccountAsync(user));
    }

    public async Task<AccountResult> SignInAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrEmpty(command.Password))
        {
            return AccountResult.Failed(
                AccountFailureReason.InvalidInput,
                "E-mail and password are required.");
        }

        var email = command.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Constant-ish work factor: do not leak account existence through timing.
            _userManager.PasswordHasher.VerifyHashedPassword(
                new ApplicationUser(),
                DummyPasswordHash,
                command.Password);

            await AuditAsync(
                AuditActions.UserLoginFailed,
                AuditOutcome.Failure,
                cancellationToken,
                actorIdentifier: email,
                details: "unknown-account");

            return AccountResult.Failed(AccountFailureReason.InvalidCredentials, "Invalid e-mail or password.");
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            command.Password,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            await AuditAsync(
                AuditActions.UserLoginFailed,
                AuditOutcome.Denied,
                cancellationToken,
                actorUserId: user.Id,
                actorIdentifier: email,
                targetId: user.Id.ToString(),
                details: "locked-out");

            return AccountResult.Failed(
                AccountFailureReason.LockedOut,
                "This account is temporarily locked because of repeated failed sign-in attempts.");
        }

        if (!signInResult.Succeeded)
        {
            await AuditAsync(
                AuditActions.UserLoginFailed,
                AuditOutcome.Failure,
                cancellationToken,
                actorUserId: user.Id,
                actorIdentifier: email,
                targetId: user.Id.ToString(),
                details: signInResult.IsNotAllowed ? "not-allowed" : "invalid-credentials");

            return AccountResult.Failed(AccountFailureReason.InvalidCredentials, "Invalid e-mail or password.");
        }

        user.LastLoginAtUtc = _timeProvider.GetUtcNow();
        await _userManager.UpdateAsync(user);

        await _signInManager.SignInAsync(user, isPersistent: false);

        await AuditAsync(
            AuditActions.UserLoginSucceeded,
            AuditOutcome.Success,
            cancellationToken,
            actorUserId: user.Id,
            actorIdentifier: email,
            targetId: user.Id.ToString());

        return AccountResult.Success(await ToAccountAsync(user));
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var current = await GetCurrentAccountEntityAsync();
        await _signInManager.SignOutAsync();

        if (current is not null)
        {
            await AuditAsync(
                AuditActions.UserLogout,
                AuditOutcome.Success,
                cancellationToken,
                actorUserId: current.Id,
                actorIdentifier: current.Email,
                targetId: current.Id.ToString());
        }
    }

    public async Task<UserAccount?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentAccountEntityAsync();
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<PagedResult<UserAccount>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize) = Paging.Normalize(page, pageSize);

        var total = await _dbContext.Users.CountAsync(cancellationToken);
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.CreatedAtUtc)
            .Skip(Paging.Skip(normalizedPage, normalizedSize))
            .Take(normalizedSize)
            .ToListAsync(cancellationToken);

        var accounts = new List<UserAccount>(users.Count);
        foreach (var user in users)
        {
            accounts.Add(await ToAccountAsync(user));
        }

        return new PagedResult<UserAccount>(accounts, normalizedPage, normalizedSize, total);
    }

    private async Task<bool> IsFirstAccountAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.AnyAsync(cancellationToken))
        {
            return false;
        }

        // Re-check under a process-level lock to avoid a double-administrator bootstrap race.
        await BootstrapLock.WaitAsync(cancellationToken);
        try
        {
            return !await _dbContext.Users.AnyAsync(cancellationToken);
        }
        finally
        {
            BootstrapLock.Release();
        }
    }

    private async Task<IdentityResult> AssignRoleAsync(ApplicationUser user, string role)
    {
        if (!await _roleManager.RoleExistsAsync(role))
        {
            var createRole = await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!createRole.Succeeded)
            {
                return createRole;
            }
        }

        return await _userManager.AddToRoleAsync(user, role);
    }

    private async Task<ApplicationUser?> GetCurrentAccountEntityAsync()
    {
        var principal = _signInManager.Context.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return await _userManager.GetUserAsync(principal);
    }

    private async Task<UserAccount> ToAccountAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);

        return new UserAccount(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            roles.Order(StringComparer.Ordinal).ToArray(),
            user.CreatedAtUtc,
            user.LastLoginAtUtc);
    }

    private async Task AuditAsync(
        string action,
        AuditOutcome outcome,
        CancellationToken cancellationToken,
        Guid? actorUserId = null,
        string? actorIdentifier = null,
        string? targetId = null,
        string? details = null)
    {
        try
        {
            var entry = AuditLogEntry.Create(
                action,
                targetType: "user",
                outcome,
                occurredAtUtc: _timeProvider.GetUtcNow(),
                actorUserId: actorUserId,
                actorIdentifier: actorIdentifier,
                targetId: targetId,
                ipAddress: _requestContext.IpAddress,
                userAgent: _requestContext.UserAgent,
                details: details);

            await _auditLog.WriteAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            // Auditing must not break authentication, but a failure is never silent.
            _logger.LogError(exception, "Failed to write audit record for action {Action}.", action);
        }
    }

    private static List<string> ValidateRegistration(RegisterAccountCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            errors.Add("E-mail is required.");
        }
        else if (command.Email.Trim().Length > 320 || !IsValidEmail(command.Email.Trim()))
        {
            errors.Add("E-mail is not a valid address.");
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            errors.Add("Password is required.");
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            errors.Add("Display name is required.");
        }
        else if (command.DisplayName.Trim().Length is < 2 or > ApplicationUser.DisplayNameMaxLength)
        {
            errors.Add($"Display name must be between 2 and {ApplicationUser.DisplayNameMaxLength} characters.");
        }

        return errors;
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
