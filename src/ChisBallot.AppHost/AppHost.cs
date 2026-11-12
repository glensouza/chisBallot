// Runs the whole of chisBallot on a laptop with one command:
//
//   dotnet run --project src/ChisBallot.AppHost
//
// It starts SQL Server in a container, applies the database scripts, creates
// the two database users and a practice election, then starts the voting API
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

IResourceBuilder<SqlServerServerResource> sql = builder.AddSqlServer("sql");
// Contained, so the two database users can have passwords of their own, as they
// do in Azure SQL.
IResourceBuilder<SqlServerDatabaseResource> database = sql.AddDatabase("chisballot")
    .WithCreationScript("""
        EXEC sp_configure 'show advanced options', 1; RECONFIGURE;
        EXEC sp_configure 'contained database authentication', 1; RECONFIGURE;
        CREATE DATABASE [chisballot] CONTAINMENT = PARTIAL;
        """);

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
        $"Server=127.0.0.1,{sql.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)};Database=chisballot;User ID={user};Password={password};TrustServerCertificate=true;Encrypt=false");
}

builder.AddExecutable("vote-api", "func", Path.GetFullPath("../ChisBallot.Vote.Api", builder.AppHostDirectory), "start", "--port", VoteApiPort.ToString())
    .WithHttpEndpoint(port: VoteApiPort, targetPort: VoteApiPort, name: "http", isProxied: false)
    .WithEnvironment("SqlConnectionString", ConnectionFor("chisballot_vote", VotePassword))
    .WaitForCompletion(migrator);

builder.AddExecutable("admin-api", "func", Path.GetFullPath("../ChisBallot.Admin.Api", builder.AppHostDirectory), "start", "--port", AdminApiPort.ToString())
    .WithHttpEndpoint(port: AdminApiPort, targetPort: AdminApiPort, name: "http", isProxied: false)
    .WithEnvironment("SqlConnectionString", ConnectionFor("chisballot_admin", AdminPassword))
    .WithEnvironment("CHISBALLOT_DEV_ADMIN", "1")
    .WaitForCompletion(migrator);

builder.AddProject<Projects.ChisBallot_Admin>("admin")
    .WithExternalHttpEndpoints();

builder.Build().Run();
