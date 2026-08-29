using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using KobareoCalendar.Domain;

namespace KobareoCalendar.Infrastructure;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (string.Equals(Environment.GetEnvironmentVariable("DATABASE_PROVIDER"), "SqlServer", StringComparison.OrdinalIgnoreCase))
            builder.UseSqlServer(
                "Server=localhost;Database=design;Integrated Security=True;Encrypt=False",
                sql => sql.MigrationsAssembly("KobareoCalendar.SqlServerMigrations"));
        else
            builder.UseSqlite("Data Source=design.db");
        var options = builder.Options;
        return new AppDbContext(options);
    }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ResourceEntity> Resources => Set<ResourceEntity>();
    public DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();
    public DbSet<ScheduleParticipantEntity> ScheduleParticipants => Set<ScheduleParticipantEntity>();
    public DbSet<ScheduleResourceEntity> ScheduleResources => Set<ScheduleResourceEntity>();
    public DbSet<UserTimelinePreferenceEntity> UserTimelinePreferences => Set<UserTimelinePreferenceEntity>();
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<PasswordPolicyEntity> PasswordPolicies => Set<PasswordPolicyEntity>();
    public DbSet<LoginAttemptPolicyEntity> LoginAttemptPolicies => Set<LoginAttemptPolicyEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });
        builder.Entity<PasswordPolicyEntity>(entity =>
        {
            entity.ToTable("PasswordPolicies");
            entity.HasKey(x => x.Id);
        });
        builder.Entity<LoginAttemptPolicyEntity>(entity =>
        {
            entity.ToTable("LoginAttemptPolicies");
            entity.HasKey(x => x.Id);
        });
        builder.Entity<ResourceEntity>(entity =>
        {
            entity.ToTable("Resources"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.Kind, x.Name });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        builder.Entity<ScheduleEntity>(entity =>
        {
            entity.ToTable("Schedules"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired(); entity.Property(x => x.Details).HasMaxLength(4000);
            entity.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc }); entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ScheduleParticipantEntity>(entity =>
        {
            entity.ToTable("ScheduleParticipants"); entity.HasKey(x => new { x.ScheduleId, x.UserId }); entity.HasIndex(x => x.UserId);
            entity.HasOne(x => x.Schedule).WithMany(x => x.Participants).HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ScheduleResourceEntity>(entity =>
        {
            entity.ToTable("ScheduleResources"); entity.HasKey(x => new { x.ScheduleId, x.ResourceId }); entity.HasIndex(x => x.ResourceId);
            entity.HasOne(x => x.Schedule).WithMany(x => x.Resources).HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserTimelinePreferenceEntity>(entity =>
        {
            entity.ToTable("UserTimelinePreferences"); entity.HasKey(x => x.Id);
            entity.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TargetResource).WithMany().HasForeignKey(x => x.TargetResourceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.SortOrder }).IsUnique();
        });
        builder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ScheduleTitle).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.RecipientUserId, x.IsRead, x.CreatedAtUtc });
            entity
                .HasOne(x => x.RecipientUser)
                .WithMany()
                .HasForeignKey(x => x.RecipientUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity
                .HasOne(x => x.Schedule)
                .WithMany()
                .HasForeignKey(x => x.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class PasswordPolicyEntity
{
    public int Id { get; set; } = 1;
    public int RequiredLength { get; set; } = 12;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
}

public sealed class LoginAttemptPolicyEntity
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public int MaxFailedAttempts { get; set; } = 5;
}

public sealed class ResourceEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public ResourceKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; }
}

public sealed class ScheduleEntity
{
    public Guid Id { get; set; } = Guid.NewGuid(); public required string Title { get; set; }
    public string Details { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public bool IsPrivate { get; set; }
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow; public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow; public long Version { get; set; }
    public List<ScheduleParticipantEntity> Participants { get; set; } = []; public List<ScheduleResourceEntity> Resources { get; set; } = [];
}
public sealed class ScheduleParticipantEntity { public Guid ScheduleId { get; set; } public ScheduleEntity Schedule { get; set; } = null!; public Guid UserId { get; set; } public ApplicationUser User { get; set; } = null!; }
public sealed class ScheduleResourceEntity { public Guid ScheduleId { get; set; } public ScheduleEntity Schedule { get; set; } = null!; public Guid ResourceId { get; set; } public ResourceEntity Resource { get; set; } = null!; }
public sealed class UserTimelinePreferenceEntity
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OwnerUserId { get; set; }
    public ApplicationUser OwnerUser { get; set; } = null!;
    public Guid? TargetUserId { get; set; }
    public ApplicationUser? TargetUser { get; set; }
    public Guid? TargetResourceId { get; set; }
    public ResourceEntity? TargetResource { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
}

public sealed class NotificationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientUserId { get; set; }
    public ApplicationUser RecipientUser { get; set; } = null!;
    public Guid ActorUserId { get; set; }
    public ApplicationUser ActorUser { get; set; } = null!;
    public Guid ScheduleId { get; set; }
    public ScheduleEntity Schedule { get; set; } = null!;
    public required string ScheduleTitle { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}

public static class DatabaseConfiguration
{
    public static void ConfigureDatabase(this DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var provider = configuration["DATABASE_PROVIDER"] ?? "Sqlite";
        var connection = configuration.GetConnectionString("Default")
            ?? (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("SQL ServerのConnectionStrings__Defaultが必要です。")
                : "Data Source=/data/kobareo-calendar.db");
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            options.UseSqlServer(connection, x => x.MigrationsAssembly("KobareoCalendar.SqlServerMigrations"));
        else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            options.UseSqlite(connection, x => x.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        else throw new InvalidOperationException($"未対応のDATABASE_PROVIDERです: {provider}");
    }
}
