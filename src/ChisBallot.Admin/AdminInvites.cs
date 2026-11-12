using System.ComponentModel.DataAnnotations;
using System.Text;
using ChisBallot.Admin.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ChisBallot.Admin;

/// <summary>
/// Makes admins. There is no public registration and no email: an existing
/// admin (or the create-admin command on the server) makes an account and gets
/// a one-time link, valid for three days, to pass to the new admin. The link
/// lets them choose a password, after which they must add a passkey or an
/// authenticator app before the admin pages open for them.
/// </summary>
public sealed class AdminInvites(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IConfiguration configuration)
{
    /// <summary>Creates the account (or reuses it) in the admin role and returns the link to hand over.</summary>
    public async Task<string> InviteAsync(string email)
    {
        string address = (email ?? string.Empty).Trim();
        if (!new EmailAddressAttribute().IsValid(address))
        {
            throw new ArgumentException("That is not an email address.", nameof(email));
        }

        if (!await roles.RoleExistsAsync(AdminAuth.AdminRole))
        {
            IdentityResult role = await roles.CreateAsync(new IdentityRole(AdminAuth.AdminRole));
            EnsureSucceeded(role);
        }

        ApplicationUser? user = await users.FindByEmailAsync(address);
        if (user is null)
        {
            user = new ApplicationUser { UserName = address, Email = address, EmailConfirmed = true };
            EnsureSucceeded(await users.CreateAsync(user));
        }

        if (!await users.IsInRoleAsync(user, AdminAuth.AdminRole))
        {
            EnsureSucceeded(await users.AddToRoleAsync(user, AdminAuth.AdminRole));
        }

        string token = await users.GeneratePasswordResetTokenAsync(user);
        string code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        string baseUrl = (configuration["PublicUrl"] ?? "http://localhost:5004").TrimEnd('/');
        return $"{baseUrl}/Account/ResetPassword?code={code}";
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
