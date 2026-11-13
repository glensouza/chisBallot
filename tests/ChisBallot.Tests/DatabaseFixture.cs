using System.Security.Cryptography;
using ChisBallot.Data;
using Npgsql;
using Serilog;
using Testcontainers.PostgreSql;

namespace ChisBallot.Tests;

/// <summary>
/// One real PostgreSQL in a container for the whole test run, migrated with
/// the same scripts and the same two database roles the site uses. The
/// migrator connects as an owner that can create roles but is not a
/// superuser, as it would in production, so the tests also show it does not
/// need more than that.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string DatabaseName = "chisballot_test";
    private const string OwnerName = "chisballot_owner";

    // When this is set (the pipeline runs the tests inside a container next to a
    // PostgreSQL service container) its value is a superuser connection string
    // to that database server. Otherwise the tests start their own container.
    private static readonly string? ExistingServer = Environment.GetEnvironmentVariable("CHISBALLOT_TEST_POSTGRES");

    private readonly PostgreSqlContainer? container = ExistingServer is null ? new PostgreSqlBuilder("postgres:17").Build() : null;

    public string OwnerConnectionString { get; private set; } = string.Empty;

    public string VoteConnectionString { get; private set; } = string.Empty;

    public string AdminConnectionString { get; private set; } = string.Empty;

    public BallotCaster Caster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (this.container is not null)
        {
            await this.container.StartAsync();
        }

        string serverConnectionString = ExistingServer ?? this.container!.GetConnectionString();

        string ownerPassword = NewPassword();
        await using (NpgsqlConnection superuser = new(serverConnectionString))
        {
            await superuser.OpenAsync();
            await ExecuteAsync(superuser, $"CREATE ROLE {OwnerName} WITH LOGIN CREATEROLE PASSWORD '{ownerPassword}'");
            await ExecuteAsync(superuser, $"CREATE DATABASE {DatabaseName} OWNER {OwnerName}");
        }

        NpgsqlConnectionStringBuilder owner = new(serverConnectionString)
        {
            Database = DatabaseName,
            Username = OwnerName,
            Password = ownerPassword,
        };
        this.OwnerConnectionString = owner.ConnectionString;

        string votePassword = NewPassword();
        string adminPassword = NewPassword();

        ILogger logger = new LoggerConfiguration().CreateLogger();
        await new Migrator(this.OwnerConnectionString, logger).MigrateAsync(new DatabaseUserPasswords(votePassword, adminPassword));

        this.VoteConnectionString = new NpgsqlConnectionStringBuilder(this.OwnerConnectionString) { Username = "chisballot_vote", Password = votePassword }.ConnectionString;
        this.AdminConnectionString = new NpgsqlConnectionStringBuilder(this.OwnerConnectionString) { Username = "chisballot_admin", Password = adminPassword }.ConnectionString;
        this.Caster = new BallotCaster(this.VoteConnectionString, logger);
    }

    public async Task DisposeAsync()
    {
        if (this.container is not null)
        {
            await this.container.DisposeAsync();
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string NewPassword()
    {
        return "Aa1" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
