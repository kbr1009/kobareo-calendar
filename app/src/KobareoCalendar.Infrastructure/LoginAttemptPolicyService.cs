using KobareoCalendar.Application;
using Microsoft.EntityFrameworkCore;

namespace KobareoCalendar.Infrastructure;

public sealed class LoginAttemptPolicyService(AppDbContext db) : ILoginAttemptPolicyService
{
    public async Task<LoginAttemptPolicy> GetAsync(CancellationToken cancellationToken)
    {
        var policy = await db.LoginAttemptPolicies.AsNoTracking().SingleAsync(cancellationToken);
        return new(policy.IsEnabled, policy.MaxFailedAttempts);
    }

    public async Task<LoginAttemptPolicy> UpdateAsync(
        LoginAttemptPolicy value,
        CancellationToken cancellationToken
    )
    {
        var policy = await db.LoginAttemptPolicies.SingleAsync(cancellationToken);
        policy.IsEnabled = value.IsEnabled;
        policy.MaxFailedAttempts = value.MaxFailedAttempts;
        if (!value.IsEnabled)
        {
            await db.Users
                .Where(user => user.AccessFailedCount != 0 || user.LockoutEnd != null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(user => user.AccessFailedCount, 0)
                        .SetProperty(user => user.LockoutEnd, (DateTimeOffset?)null),
                    cancellationToken
                );
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(policy.IsEnabled, policy.MaxFailedAttempts);
    }
}
