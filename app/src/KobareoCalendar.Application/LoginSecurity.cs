namespace KobareoCalendar.Application;

public sealed record LoginAttemptPolicy(bool IsEnabled, int MaxFailedAttempts);

public enum LoginStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    Failed,
}

public sealed record AuthenticatedUser(
    Guid Id,
    string DisplayName,
    string Email,
    string Role,
    bool IsActive
);

public sealed record LoginResult(LoginStatus Status, AuthenticatedUser? User = null);

public interface ILoginService
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);
}

public interface ILoginAttemptPolicyService
{
    Task<LoginAttemptPolicy> GetAsync(CancellationToken cancellationToken);

    Task<LoginAttemptPolicy> UpdateAsync(
        LoginAttemptPolicy policy,
        CancellationToken cancellationToken
    );
}
