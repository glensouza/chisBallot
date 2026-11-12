// Runs the whole of chisBallot on a laptop with one command:
//
//   dotnet run --project src/ChisBallot.AppHost
//
// It starts PostgreSQL in a container, applies the database scripts, creates
// the two database roles and a practice election, then starts the voting API
// and site and the admin API and site. Needs Docker and the Azure Functions
// Core Tools (`func`).
//
// Everything here is for local development. The passwords below protect a
// throwaway container that is deleted when you stop; nothing deployed uses
// them.

using Aspire.Hosting.ApplicationModel;

const string VotePassword = "Local-Vote-Pw-1!";
const string AdminPassword = "Local-Admin-Pw-1!";
const int VoteApiPort = 7071;
const int AdminApiPort = 7072;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<PostgresServerResource> sql = builder.AddPostgres("sql");
IResourceBuilder<PostgresDatabaseResource> database = sql.AddDatabase("chisballot");

IResourceBuilder<ProjectResource> vote = builder.AddProject<Projects.ChisBallot_Vote>("vote")
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

builder.AddExecutable("vote-api", "func", Path.GetFullPath("../ChisBallot.Vote.Api", builder.AppHostDirectory), "start", "--port", VoteApiPort.ToString())
    .WithHttpEndpoint(port: VoteApiPort, targetPort: VoteApiPort, name: "http", isProxied: false)
    .WithEnvironment("DatabaseConnectionString", ConnectionFor("chisballot_vote", VotePassword))
    .WaitForCompletion(migrator);

builder.AddExecutable("admin-api", "func", Path.GetFullPath("../ChisBallot.Admin.Api", builder.AppHostDirectory), "start", "--port", AdminApiPort.ToString())
    .WithHttpEndpoint(port: AdminApiPort, targetPort: AdminApiPort, name: "http", isProxied: false)
    .WithEnvironment("DatabaseConnectionString", ConnectionFor("chisballot_admin", AdminPassword))
    .WithEnvironment("CHISBALLOT_DEV_ADMIN", "1")
    .WaitForCompletion(migrator);

builder.AddProject<Projects.ChisBallot_Admin>("admin")
    .WithExternalHttpEndpoints();

builder.Build().Run();
