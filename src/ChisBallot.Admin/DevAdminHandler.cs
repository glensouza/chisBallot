namespace ChisBallot.Admin;
#if DEBUG
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>
/// Signs everyone in as an admin called "Local developer". It exists only in
/// Debug builds, is switched on by an environment variable, and the build
/// pipeline checks that no Release build contains it.
/// </summary>
internal sealed class DevAdminHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAdmin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "Local developer"), new Claim(ClaimTypes.Role, AdminAuth.AdminRole)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
#endif
