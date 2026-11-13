namespace ChisBallot.Admin;

using ChisBallot.Admin.Account;
using ChisBallot.Admin.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Who may use the admin site: a signed-in, invited admin who has set up a
/// second factor (a passkey, or an authenticator app). Accounts live in a small
/// SQLite file kept apart from the ballot database. There is no public
/// registration: admins are invited by an existing admin, or created from the
/// server with the create-admin command.
/// </summary>
public static class AdminAuth
{
    public const string PolicyName = "Admin";
    public const string AdminRole = "admin";

    public static void AddAdminAuth(this WebApplicationBuilder builder)
    {
        string identityDatabase = builder.Configuration["IdentityDatabasePath"] ?? "identity.db";
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={identityDatabase}"));

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();

        bool developmentAdmin = false;
#if DEBUG
        // Local development only. This block is compiled out of Release
        // builds, which are the only ones that are deployed.
        developmentAdmin = Environment.GetEnvironmentVariable("CHISBALLOT_DEV_ADMIN") == "1";
#endif

        AuthenticationBuilder authentication = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            });
        authentication.AddIdentityCookies();

        if (!developmentAdmin)
        {
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        }
#if DEBUG
        else
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevAdminHandler>(DevAdminHandler.SchemeName, null);
            builder.Services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = DevAdminHandler.SchemeName;
                options.DefaultAuthenticateScheme = DevAdminHandler.SchemeName;
                options.DefaultChallengeScheme = DevAdminHandler.SchemeName;
            });
        }
#endif

        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // An invitation link works for three days.
        builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromDays(3));

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            if (!builder.Environment.IsDevelopment())
            {
                options.Cookie.Name = "__Host-chisballot-admin";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            }
        });

        builder.Services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
        builder.Services.AddScoped<AdminInvites>();
        builder.Services.AddAuthorizationBuilder().AddPolicy(PolicyName, policy => policy.AddRequirements(new AdminRequirement()));
    }
}
