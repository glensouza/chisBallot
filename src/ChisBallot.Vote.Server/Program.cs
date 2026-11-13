using ChisBallot.Data;
using ChisBallot.Vote.Server;
using Serilog;
using Serilog.Events;

// The public voting site: the browser app plus the three endpoints it needs
// (see VoteEndpoints). It connects as the chisballot_vote database role, which
// cannot read ballots, so nothing here can look at how anyone voted.

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Per-request logging is off on purpose: it would record paths and
    // timing for the cast-ballot request.
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(Log.Logger);

string connectionString = builder.Configuration["DatabaseConnectionString"]
    ?? throw new InvalidOperationException("DatabaseConnectionString is not set.");

builder.Services.AddSingleton(new ElectionReader(connectionString));
builder.Services.AddSingleton(new ResultsReader(connectionString));
builder.Services.AddSingleton(new AuditReader(connectionString));
builder.Services.AddSingleton(new BallotCaster(connectionString, Log.Logger));
builder.Services.AddSingleton(new DemoSlips(connectionString));

WebApplication app = builder.Build();

app.UseMiddleware<SecurityHeaders>();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.MapStaticAssets();
app.MapVoteEndpoints();
app.MapFallbackToFile("index.html");

app.Run();
