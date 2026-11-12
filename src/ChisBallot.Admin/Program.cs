using ChisBallot.Admin;
using ChisBallot.Data;
using Serilog;
using Serilog.Events;

// The admin site: Blazor Server. It calls the data layer directly, as the
// chisballot_admin database role, which cannot read ballots.

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(Log.Logger);

string connectionString = builder.Configuration["DatabaseConnectionString"]
    ?? throw new InvalidOperationException("DatabaseConnectionString is not set.");

builder.Services.AddSingleton(new ElectionReader(connectionString));
builder.Services.AddSingleton(new ElectionAdmin(connectionString, Log.Logger));
builder.Services.AddSingleton(new ResultsReader(connectionString));
builder.Services.AddSingleton<AdminViews>();

// Slips are held in a page's memory while they are shown once. Do not keep a
// dropped connection's page around for long.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1));

builder.AddAdminAuth();

WebApplication app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<ChisBallot.Admin.App>().AddInteractiveServerRenderMode();

app.Run();
