using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PharmaERP.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core CLI tooling (e.g. dotnet ef migrations add, dotnet ef database update).
///
/// SEPARATION OF RESPONSIBILITIES:
/// - Runtime application configuration resolves through IDatabaseConfigStore and Windows DPAPI in %LocalAppData%\PharmaERP\settings.json.
/// - Design-time EF tooling does NOT silently read end-user encrypted credentials.
///
/// DEVELOPER PRECEDENCE:
/// 1. PHARMAERP_CONNECTIONSTRING environment variable (ideal for CI/CD or custom dev databases).
/// 2. Documented developer default: Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("PHARMAERP_CONNECTIONSTRING")
            ?? "Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });

        return new AppDbContext(optionsBuilder.Options);
    }
}

