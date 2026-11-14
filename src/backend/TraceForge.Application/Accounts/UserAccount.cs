namespace TraceForge.Application.Accounts;

/// <summary>Public projection of a user account. Never includes password hashes or tokens.</summary>
public sealed record UserAccount(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

/// <summary>Registration input. Validated by the application service, not only by the transport.</summary>
public sealed record RegisterAccountCommand(string Email, string Password, string DisplayName);

/// <summary>Credential sign-in input.</summary>
public sealed record SignInCommand(string Email, string Password);

public enum AccountFailureReason
{
    None = 0,
    InvalidInput = 1,
    InvalidCredentials = 2,
    LockedOut = 3,
    RegistrationDisabled = 4,
    DuplicateAccount = 5,
    WeakPassword = 6,
    NotAuthenticated = 7,
}

public sealed record AccountResult(
    bool Succeeded,
    UserAccount? User,
    AccountFailureReason Failure,
    IReadOnlyList<string> Errors)
{
    public static AccountResult Success(UserAccount user) =>
        new(true, user, AccountFailureReason.None, []);

    public static AccountResult Failed(AccountFailureReason reason, params string[] errors) =>
        new(false, null, reason, errors);
}
