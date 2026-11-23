using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Reqviora.Infrastructure.Persistence;

namespace Reqviora.SqlServer.Persistence;

public sealed class SqlServerReqvioraDbContext(
    DbContextOptions<SqlServerReqvioraDbContext> options) : ReqvioraDbContext(options);

public sealed class SqlServerReqvioraDbContextFactory : IDesignTimeDbContextFactory<SqlServerReqvioraDbContext>
{
    public SqlServerReqvioraDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("REQVIORA_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set REQVIORA_SQLSERVER_CONNECTION for the SQL Server tooling.");
        var options = new DbContextOptionsBuilder<SqlServerReqvioraDbContext>()
            .UseSqlServer(connection)
            .Options;
        return new SqlServerReqvioraDbContext(options);
    }
}
