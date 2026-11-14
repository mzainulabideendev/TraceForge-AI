namespace TraceForge.Application.Accounts;

/// <summary>
/// Account use cases backed by ASP.NET Core Identity. The implementation owns credential
/// verification, lockout handling, role assignment, sign-in cookies and audit writes.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Registers an account. Self-registration may be disabled; the first account of a fresh
    /// installation is promoted to Administrator so the system is usable.
    /// </summary>
    Task<AccountResult> RegisterAsync(RegisterAccountCommand command, CancellationToken cancellationToken);

    /// <summary>Verifies credentials, applies lockout policy, signs the user in and audits the attempt.</summary>
    Task<AccountResult> SignInAsync(SignInCommand command, CancellationToken cancellationToken);

    /// <summary>Signs the current user out and audits the action.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);

    /// <summary>Returns the currently authenticated account, or null when there is no session.</summary>
    Task<UserAccount?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Lists accounts. Authorized for administrators at the API boundary.</summary>
    Task<Common.PagedResult<UserAccount>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}
