using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reqviora.Infrastructure.Persistence;
using Reqviora.SqlServer.Persistence;

namespace Reqviora.Infrastructure.Tests;

internal sealed class PersistenceDatabase : IAsyncDisposable
{
    private readonly Func<ReqvioraDbContext> open;
    private readonly SqliteConnection? sqlite;

    private PersistenceDatabase(Func<ReqvioraDbContext> open, SqliteConnection? sqlite = null)
    {
        this.open = open;
        this.sqlite = sqlite;
    }

    public ReqvioraDbContext Open() => open();

    public static async Task<PersistenceDatabase> CreateAsync(string provider, string? migration = null)
    {
        var database = await ConfigureAsync(provider);
        try
        {
            await using var db = database.Open();
            if (provider == "sqlite")
                await db.Database.EnsureCreatedAsync();
            else
                await db.Database.MigrateAsync(migration);

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public static ReqvioraDbContext SqlServer(string connectionString) =>
        new SqlServerReqvioraDbContext(
            new DbContextOptionsBuilder<SqlServerReqvioraDbContext>().UseSqlServer(connectionString).Options);

    private static async Task<PersistenceDatabase> ConfigureAsync(string provider)
    {
        var name = $"reqviora_rashmina_{Guid.NewGuid():N}";
        if (provider == "postgres")
        {
            var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("REQVIORA_TEST_POSTGRES"))
            {
                Database = name,
                Pooling = false
            };
            RequireLocalHost(connection.Host);
            return new(() => new ReqvioraDbContext(
                new DbContextOptionsBuilder<ReqvioraDbContext>().UseNpgsql(connection.ConnectionString).Options));
        }

        if (provider == "sqlserver")
        {
            var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("REQVIORA_TEST_SQLSERVER"))
            {
                InitialCatalog = name,
                Pooling = false
            };
            RequireLocalHost(connection.DataSource.Split(',')[0]);
            return new(() => SqlServer(connection.ConnectionString));
        }

        if (provider != "sqlite")
            throw new ArgumentOutOfRangeException(nameof(provider));

        var sqlite = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await sqlite.OpenAsync();
        return new(() => new ReqvioraDbContext(
            new DbContextOptionsBuilder<ReqvioraDbContext>().UseSqlite(sqlite).Options), sqlite);
    }

    private static void RequireLocalHost(string? server)
    {
        if (server is not ("127.0.0.1" or "localhost"))
            throw new InvalidOperationException("Integration tests need a local database server.");
    }

    public async ValueTask DisposeAsync()
    {
        if (sqlite is not null)
        {
            await sqlite.DisposeAsync();
            return;
        }

        await using var db = Open();
        await db.Database.EnsureDeletedAsync();
    }
}
