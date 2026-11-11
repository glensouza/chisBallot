using System.Security.Cryptography;
using ChisBallot.Data;
using Microsoft.Data.SqlClient;
using Serilog;
using Testcontainers.MsSql;

namespace ChisBallot.Tests;

/// <summary>
/// One real SQL Server in a container for the whole test run, migrated with
/// the same scripts and the same two database users the site uses.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string DatabaseName = "chisballot_test";

    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string OwnerConnectionString { get; private set; } = string.Empty;

    public string VoteConnectionString { get; private set; } = string.Empty;

    public string AdminConnectionString { get; private set; } = string.Empty;

    public BallotCaster Caster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        await using (SqlConnection master = new(this.container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using SqlCommand create = new($"CREATE DATABASE {DatabaseName}", master);
            await create.ExecuteNonQueryAsync();
        }

        SqlConnectionStringBuilder owner = new(this.container.GetConnectionString()) { InitialCatalog = DatabaseName, TrustServerCertificate = true };
        this.OwnerConnectionString = owner.ConnectionString;

        string votePassword = NewPassword();
        string adminPassword = NewPassword();

        ILogger logger = new LoggerConfiguration().CreateLogger();
        await new Migrator(this.OwnerConnectionString, logger).MigrateAsync(new DatabaseUserPasswords(votePassword, adminPassword));

        this.VoteConnectionString = new SqlConnectionStringBuilder(this.OwnerConnectionString) { UserID = "chisballot_vote", Password = votePassword }.ConnectionString;
        this.AdminConnectionString = new SqlConnectionStringBuilder(this.OwnerConnectionString) { UserID = "chisballot_admin", Password = adminPassword }.ConnectionString;
        this.Caster = new BallotCaster(this.VoteConnectionString, logger);
    }

    public async Task DisposeAsync()
    {
        await this.container.DisposeAsync();
    }

    private static string NewPassword()
    {
        return "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
