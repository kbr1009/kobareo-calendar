using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using KobareoCalendar.Domain;

namespace KobareoCalendar.Infrastructure;

public static class IdentitySeeder
{
    public static async Task MigrateAndSeedIdentityAsync(this IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await SeedIdentityAsync(scope.ServiceProvider, configuration, db);
    }

    public static async Task SeedIdentityAsync(this IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedIdentityAsync(scope.ServiceProvider, configuration, db);
    }

    private static async Task SeedIdentityAsync(
        IServiceProvider services,
        IConfiguration configuration,
        AppDbContext db)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Admin", "User" })
            if (!await roles.RoleExistsAsync(role))
                IdentityResultGuard(await roles.CreateAsync(new IdentityRole<Guid>(role)));

        if (!await db.PasswordPolicies.AnyAsync())
        {
            db.PasswordPolicies.Add(new PasswordPolicyEntity());
            await db.SaveChangesAsync();
        }
        if (!await db.LoginAttemptPolicies.AnyAsync())
        {
            db.LoginAttemptPolicies.Add(new LoginAttemptPolicyEntity());
            await db.SaveChangesAsync();
        }

        var email = configuration["SEED_ADMIN_EMAIL"] ?? "admin@example.com";
        var password = configuration["SEED_ADMIN_PASSWORD"];
        var environment = configuration["ASPNETCORE_ENVIRONMENT"];
        if (string.IsNullOrWhiteSpace(password))
        {
            if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SEED_ADMIN_PASSWORDは本番環境で必須です。");
            password = "ChangeMe123!";
        }
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "管理者", IsActive = true };
            IdentityResultGuard(await users.CreateAsync(admin, password));
        }
        if (!await users.IsInRoleAsync(admin, "Admin")) IdentityResultGuard(await users.AddToRoleAsync(admin, "Admin"));

        if (!await db.Resources.AnyAsync())
        {
            db.Resources.AddRange(
                new ResourceEntity { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "会議室 1", Kind = ResourceKind.Room },
                new ResourceEntity { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "プロジェクター", Kind = ResourceKind.Equipment });
            await db.SaveChangesAsync();
        }
    }

    private static void IdentityResultGuard(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
