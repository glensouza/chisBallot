using ChisBallot.Admin;
using ChisBallot.Admin.Data;
using ChisBallot.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

// The admin site: Blazor Server. It calls the data layer directly, as the
// chisballot_admin database role, which cannot read ballots.
//
//   dotnet ChisBallot.Admin.dll create-admin <email>
//
// makes the first admin (or a new one) and prints a one-time link for them.

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

// Sign-in cookies and anti-forgery tokens survive a restart only if the keys are kept.
if (builder.Configuration["DataProtectionKeyPath"] is string keyPath)
{
    builder.Services.AddDataProtection().SetApplicationName("chisballot-admin").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

// The site sits behind a Cloudflare tunnel on this machine, which says whether the visitor used HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // A pull-request preview sits behind a router in another container, reachable only on a private
    // network. Production leaves this off and trusts only the tunnel on this machine.
    if (builder.Configuration.GetValue("TrustAnyProxy", false))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.AddAdminAuth();

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

    if (args is ["create-admin", string email])
    {
        string link = await scope.ServiceProvider.GetRequiredService<AdminInvites>().InviteAsync(email);
        Console.WriteLine($"Admin account ready for {email}. Give them this link (valid for three days, works once):");
        Console.WriteLine(link);
        return;
    }
}

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";

    // Scripts, styles, images, fonts and connections come from this site only (the live pages talk
    // to the server over a WebSocket on the same address). Inline styles are allowed, scripts never.
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self' ws: wss:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

app.MapStaticAssets();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<ChisBallot.Admin.App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();

app.Run();
