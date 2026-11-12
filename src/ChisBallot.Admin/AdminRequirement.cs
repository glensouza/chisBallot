using ChisBallot.Admin.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace ChisBallot.Admin;

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// An admin passes only if the account is in the admin role and has a second
/// factor set up (a passkey, which is two factors in one, or an authenticator
/// app). Both are read from the account store on every check, not from the
/// sign-in cookie, so removing someone's admin role takes effect at once.
/// </summary>
public sealed class AdminRequirementHandler(UserManager<ApplicationUser> users) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

#if DEBUG
        if (context.User.Identity.AuthenticationType == DevAdminHandler.SchemeName)
        {
            context.Succeed(requirement);
            return;
        }
#endif

        ApplicationUser? user = await users.GetUserAsync(context.User);
        if (user is null || !await users.IsInRoleAsync(user, AdminAuth.AdminRole))
        {
            return;
        }

        if (await users.GetTwoFactorEnabledAsync(user) || (await users.GetPasskeysAsync(user)).Count != 0)
        {
            context.Succeed(requirement);
        }
    }
}
