using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ChisBallot.Admin;

/// <summary>
/// Who may use the admin site: only a signed-in user in the admin role. Every
/// admin page asks for the Admin policy. Until sign-in exists, nobody can
/// satisfy it, so the site refuses everyone (it fails closed).
/// </summary>
public static class AdminAuth
{
    public const string PolicyName = "Admin";
    public const string AdminRole = "admin";

    public static void AddAdminAuth(this WebApplicationBuilder builder)
    {
        ConfigureAuthentication(builder.Services);
        builder.Services.AddAuthorizationBuilder().AddPolicy(PolicyName, policy => policy.RequireRole(AdminRole));
        builder.Services.AddCascadingAuthenticationState();
    }

    private static void ConfigureAuthentication(IServiceCollection services)
    {
#if DEBUG
        // Local development only. This block is compiled out of Release
        // builds, which are the only ones that are deployed.
        if (Environment.GetEnvironmentVariable("CHISBALLOT_DEV_ADMIN") == "1")
        {
            services.AddAuthentication(DevAdminHandler.Scheme).AddScheme<AuthenticationSchemeOptions, DevAdminHandler>(DevAdminHandler.Scheme, null);
            return;
        }
#endif
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
    }
}
