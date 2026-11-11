using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Serilog;

namespace ChisBallot.Data;

public sealed record DatabaseUserPasswords(string Vote, string Admin);

/// <summary>
/// Applies the embedded SQL scripts in name order and records each one in
/// dbo.SchemaVersions. The two database users' passwords come from the
/// deployment and are substituted in memory; they are not logged.
/// </summary>
public sealed partial class Migrator(string connectionString, ILogger logger)
{
    private const string ScriptFolder = ".Migrations.";

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    public async Task MigrateAsync(DatabaseUserPasswords passwords, CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, null, """
            IF OBJECT_ID('dbo.SchemaVersions') IS NULL
                CREATE TABLE dbo.SchemaVersions (Script nvarchar(200) NOT NULL PRIMARY KEY);
            """, cancellationToken);

        // Azure SQL supports database users with passwords directly. A local
        // SQL Server needs contained database authentication switched on.
        await ExecuteAsync(connection, null, """
            IF CAST(SERVERPROPERTY('EngineEdition') AS int) <> 5
            BEGIN
                EXEC sp_configure 'show advanced options', 1; RECONFIGURE;
                EXEC sp_configure 'contained database authentication', 1; RECONFIGURE;
                DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET CONTAINMENT = PARTIAL';
                EXEC (@sql);
            END
            """, cancellationToken);

        List<string> applied = [];
        await using (SqlCommand list = new("SELECT Script FROM dbo.SchemaVersions", connection))
        await using (SqlDataReader reader = await list.ExecuteReaderAsync(cancellationToken))
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
            string sql = (await streamReader.ReadToEndAsync(cancellationToken))
                .Replace("$(VotePassword)", Escape(passwords.Vote), StringComparison.Ordinal)
                .Replace("$(AdminPassword)", Escape(passwords.Admin), StringComparison.Ordinal);

            await using SqlTransaction transaction = connection.BeginTransaction();
            foreach (string batch in BatchSeparator().Split(sql))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await ExecuteAsync(connection, transaction, batch, cancellationToken);
                }
            }

            await ExecuteAsync(connection, transaction, $"INSERT dbo.SchemaVersions (Script) VALUES (N'{script}')", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.Information("Applied migration {Script}", script);
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
