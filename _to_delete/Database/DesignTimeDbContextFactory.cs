using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DantesRoleplay.Database;

/// <summary>
/// Lets `dotnet ef` run against this class library directly, without needing the MCP host as a
/// startup project. Design-time only — never used at runtime.
///
///     dotnet ef migrations add Initial --project DantesRoleplay
///     dotnet ef database update --project DantesRoleplay
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DantesRoleplayDbContext>
{
    public DantesRoleplayDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<DantesRoleplayDbContext>();
        builder.UseSqlite("Data Source=dantesroleplay-design.db");
        return new DantesRoleplayDbContext(builder.Options);
    }
}
