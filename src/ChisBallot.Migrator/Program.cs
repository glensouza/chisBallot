using ChisBallot.Data;
using ChisBallot.Migrator;
using Serilog;
using ILogger = Serilog.ILogger;

// Applies the database scripts and creates the two database users.
//
//   ConnectionStrings__chisballot   owner connection string (required)
//   ChisBallot__VotePassword        password for chisballot_vote (required)
//   ChisBallot__AdminPassword       password for chisballot_admin (required)
//
// With --seed-dev it also creates a practice election with a few slips, for
// running the sites on a laptop. Never use --seed-dev against a real database.

ILogger logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__chisballot");
string? votePassword = Environment.GetEnvironmentVariable("ChisBallot__VotePassword");
string? adminPassword = Environment.GetEnvironmentVariable("ChisBallot__AdminPassword");

if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(votePassword) || string.IsNullOrEmpty(adminPassword))
{
    logger.Error("ConnectionStrings__chisballot, ChisBallot__VotePassword and ChisBallot__AdminPassword must all be set.");
    return 1;
}

await new ChisBallot.Data.Migrator(connectionString, logger).MigrateAsync(new DatabaseUserPasswords(votePassword, adminPassword));

if (args.Contains("--seed-dev"))
{
    string siteUrl = Environment.GetEnvironmentVariable("ChisBallot__VoteSiteUrl") ?? "http://localhost:5173";
    await DevSeed.RunAsync(connectionString, siteUrl, logger);
}

return 0;
