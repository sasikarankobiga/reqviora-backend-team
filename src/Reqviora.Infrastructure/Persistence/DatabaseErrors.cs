using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Reqviora.Infrastructure.Persistence;

internal static class DatabaseErrors
{
    internal static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            or SqlException { Number: 2601 or 2627 };
}
