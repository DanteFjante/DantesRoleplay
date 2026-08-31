using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DantesRoleplay.Web.Persistence;

public sealed class WebContentDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<WebContentDbContext>
{
    public WebContentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(
                "Data Source=dantesroleplay-web-design.db",
                sqlite => sqlite.MigrationsHistoryTable("__web_migrations_history"))
            .Options;
        return new WebContentDbContext(options);
    }
}
