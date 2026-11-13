namespace ChisBallot.Data;

using System.Reflection;
using Npgsql;
using NpgsqlTypes;
using Serilog;

/// <summary>
/// Applies the embedded SQL scripts in name order and records each one in
/// ballot.schema_versions. The two database roles are created (or have their
/// password refreshed) from the deployment's passwords before the scripts
/// run, so the passwords never appear in a script and are not logged.
/// </summary>
public sealed class Migrator(string connectionString, ILogger logger)
{
    private const string ScriptFolder = ".Migrations.";

    public async Task MigrateAsync(DatabaseUserPasswords passwords, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, null, """
            CREATE SCHEMA IF NOT EXISTS ballot;
            CREATE TABLE IF NOT EXISTS ballot.schema_versions (script text NOT NULL PRIMARY KEY);
            """, cancellationToken);

        await EnsureRoleAsync(connection, "chisballot_vote", passwords.Vote, cancellationToken);
        await EnsureRoleAsync(connection, "chisballot_admin", passwords.Admin, cancellationToken);

        List<string> applied = [];
        await using (NpgsqlCommand list = new("SELECT script FROM ballot.schema_versions", connection))
        await using (NpgsqlDataReader reader = await list.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }

        Assembly assembly = typeof(Migrator).Assembly;
        IEnumerable<string> resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (string resource in resources)
        {
            string script = resource[(resource.LastIndexOf(ScriptFolder, StringComparison.Ordinal) + ScriptFolder.Length)..];
            if (applied.Contains(script))
            {
                continue;
            }

            await using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader streamReader = new(stream);
            string sql = await streamReader.ReadToEndAsync(cancellationToken);

            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, sql, cancellationToken);

            await using (NpgsqlCommand record = new("INSERT INTO ballot.schema_versions (script) VALUES (@script)", connection, transaction))
            {
                record.Parameters.AddWithValue("script", script);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            logger.Information("Applied migration {Script}", script);
        }
    }

    /// <summary>
    /// Creates the role, or refreshes its password if it exists. The server
    /// builds the statement with format(%I, %L), so the name and password are
    /// quoted by PostgreSQL itself.
    /// </summary>
    private static async Task EnsureRoleAsync(NpgsqlConnection connection, string name, string password, CancellationToken cancellationToken)
    {
        string statement;
        await using (NpgsqlCommand build = new("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)
                        THEN format('ALTER ROLE %I WITH LOGIN PASSWORD %L', @name, @password)
                        ELSE format('CREATE ROLE %I WITH LOGIN PASSWORD %L', @name, @password) END
            """, connection))
        {
            build.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = name });
            build.Parameters.Add(new NpgsqlParameter("password", NpgsqlDbType.Text) { Value = password });
            statement = (string)(await build.ExecuteScalarAsync(cancellationToken))!;
        }

        await ExecuteAsync(connection, null, statement, cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
