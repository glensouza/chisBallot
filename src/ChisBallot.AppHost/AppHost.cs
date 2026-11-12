// Runs the whole of chisBallot on a laptop with one command:
//
//   dotnet run --project src/ChisBallot.AppHost
//
// It starts PostgreSQL in a container, applies the database scripts, creates
// the two database roles and a practice election, then starts the voting site
// and the admin site. Needs Docker.
//
// Everything here is for local development. The passwords below protect a
// throwaway container that is deleted when you stop; nothing deployed uses
// them.

using Aspire.Hosting.ApplicationModel;

const string VotePassword = "Local-Vote-Pw-1!";
const string AdminPassword = "Local-Admin-Pw-1!";

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<PostgresServerResource> sql = builder.AddPostgres("sql");
IResourceBuilder<PostgresDatabaseResource> database = sql.AddDatabase("chisballot");

IResourceBuilder<ProjectResource> vote = builder.AddProject<Projects.ChisBallot_Vote_Server>("vote")
    .WithExternalHttpEndpoints();

IResourceBuilder<ProjectResource> migrator = builder.AddProject<Projects.ChisBallot_Migrator>("migrator")
    .WithReference(database)
    .WithEnvironment("ChisBallot__VoteSiteUrl", vote.GetEndpoint("http"))
    .WaitFor(database)
    .WithEnvironment("ChisBallot__VotePassword", VotePassword)
    .WithEnvironment("ChisBallot__AdminPassword", AdminPassword)
    .WithArgs("--seed-dev");

ReferenceExpression ConnectionFor(string user, string password)
{
    return ReferenceExpression.Create(
        $"Host=127.0.0.1;Port={sql.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)};Database=chisballot;Username={user};Password={password}");
}

vote.WithEnvironment("DatabaseConnectionString", ConnectionFor("chisballot_vote", VotePassword))
    .WaitForCompletion(migrator);

// Admin accounts live in a local file. By default a Debug build signs you in as
// a local developer; set CHISBALLOT_REAL_SIGNIN=1 before starting to try the
// real sign-in (create an admin first with: dotnet run --project src/ChisBallot.Admin
// -- create-admin you@example.com, using the same IdentityDatabasePath).
IResourceBuilder<ProjectResource> admin = builder.AddProject<Projects.ChisBallot_Admin>("admin")
    .WithExternalHttpEndpoints()
    .WithEnvironment("DatabaseConnectionString", ConnectionFor("chisballot_admin", AdminPassword))
    .WithEnvironment("IdentityDatabasePath", Path.GetFullPath("../../.local/identity.db", builder.AppHostDirectory))
    .WaitForCompletion(migrator);

if (Environment.GetEnvironmentVariable("CHISBALLOT_REAL_SIGNIN") != "1")
{
    admin.WithEnvironment("CHISBALLOT_DEV_ADMIN", "1");
}

builder.Build().Run();
