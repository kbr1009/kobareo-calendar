using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using KobareoCalendar.Infrastructure;

namespace KobareoCalendar.SqlServerMigrations;

public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=design;Integrated Security=True;Encrypt=False",
                sql => sql.MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.FullName))
            .Options;
        return new AppDbContext(options);
    }
}
