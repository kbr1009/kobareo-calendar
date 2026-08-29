using System.Data;
using KobareoCalendar.Application;
using KobareoCalendar.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KobareoCalendar.WebApp;

public sealed class IdentityLoginService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn
) : ILoginService
{
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return new(LoginStatus.InvalidCredentials);

        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
            return new(LoginStatus.InvalidCredentials);

        var policy = await db.LoginAttemptPolicies.AsNoTracking().SingleAsync(cancellationToken);
        if (!policy.IsEnabled)
            await ClearLockoutAsync(user.Id, cancellationToken);
        else if (await users.IsLockedOutAsync(user))
            return new(LoginStatus.LockedOut);

        var passwordResult = await signIn.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: false
        );
        if (!passwordResult.Succeeded)
        {
            if (!policy.IsEnabled)
                return new(LoginStatus.InvalidCredentials);
            return await RecordFailureAsync(user.Id, policy.MaxFailedAttempts, cancellationToken);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        var roles = await users.GetRolesAsync(user);
        return new(
            LoginStatus.Succeeded,
            new AuthenticatedUser(
                user.Id,
                user.DisplayName,
                user.Email ?? string.Empty,
                roles.FirstOrDefault() ?? "User",
                user.IsActive
            )
        );
    }

    private async Task<LoginResult> RecordFailureAsync(
        Guid userId,
        int maxFailedAttempts,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );
        await db.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.AccessFailedCount,
                    user => user.AccessFailedCount + 1
                ),
                cancellationToken
            );
        var failedAttempts = await db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.AccessFailedCount)
            .SingleAsync(cancellationToken);
        if (failedAttempts >= maxFailedAttempts)
        {
            await db.Users
                .Where(user => user.Id == userId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.LockoutEnd,
                        DateTimeOffset.UtcNow.Add(LockoutDuration)
                    ),
                    cancellationToken
                );
        }
        await transaction.CommitAsync(cancellationToken);
        return new(
            failedAttempts >= maxFailedAttempts
                ? LoginStatus.LockedOut
                : LoginStatus.InvalidCredentials
        );
    }

    private Task ClearLockoutAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user =>
                user.Id == userId && (user.AccessFailedCount != 0 || user.LockoutEnd != null)
            )
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.AccessFailedCount, 0)
                    .SetProperty(user => user.LockoutEnd, (DateTimeOffset?)null),
                cancellationToken
            );
}
