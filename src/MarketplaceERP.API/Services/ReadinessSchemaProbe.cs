using System.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Read-only readiness guard: a PostgreSQL connection can be healthy while
/// the required ERP schemas or EF migration history tables are still absent.
/// Uses parameterized to_regclass checks; never migrates or seeds data.
/// </summary>
public static class ReadinessSchemaProbe
{
    public static async Task<bool> RequiredTablesExistAsync(
        DbContext context,
        string applicationTable,
        string migrationsHistoryTable,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return false;
        var connection = context.Database.GetDbConnection();
        var closeAfterCheck = connection.State != ConnectionState.Open;
        try
        {
            if (closeAfterCheck) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass(@application_table) IS NOT NULL AND " +
                                  "to_regclass(@migrations_table) IS NOT NULL";
            var application = command.CreateParameter();
            application.ParameterName = "application_table";
            application.Value = applicationTable;
            command.Parameters.Add(application);
            var migrations = command.CreateParameter();
            migrations.ParameterName = "migrations_table";
            migrations.Value = migrationsHistoryTable;
            command.Parameters.Add(migrations);
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            if (closeAfterCheck) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Fail closed when this deployed binary and the database differ in EF migration history.
    /// Both the known migrations and database history must match exactly.
    /// Read-only: never migrates or seeds.
    /// </summary>
    public static async Task<bool> CurrentMigrationsAppliedAsync(
        DbContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return false;

        var expected = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        if (expected.Count == 0) return false;

        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return expected.SetEquals(applied);
    }
}
