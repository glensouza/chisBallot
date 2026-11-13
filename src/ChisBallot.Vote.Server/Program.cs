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

// Guessing codes and asking for demo slips in bulk are slowed down per client address.
// The counts live in memory only and are never logged. The address is the one Cloudflare
// saw (the tunnel puts it in a header); on a laptop it is the connection's own.
// Each ballot and each demo slip comes with a solved puzzle (a proof of work). It costs a script
// some work per request; it does not tell a person from a program, so it sits beside the limits.
builder.Services.AddSingleton(new ProofOfWorkVerifier(
    difficulty: builder.Configuration.GetValue("ProofOfWork:Difficulty", 16),
    lifetime: TimeSpan.FromMinutes(5)));

builder.Services.AddSingleton(new VoteLimits(
    WrongCodes: new AttemptCounter(limit: 30, window: TimeSpan.FromMinutes(10)),
    DemoSlips: new AttemptCounter(limit: 100, window: TimeSpan.FromMinutes(10))));

WebApplication app = builder.Build();

app.UseMiddleware<SecurityHeaders>();

app.MapStaticAssets();
app.MapVoteEndpoints();
app.MapFallbackToFile("index.html");

app.Run();
