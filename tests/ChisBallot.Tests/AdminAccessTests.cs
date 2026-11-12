using System.Security.Claims;
using System.Text;
using ChisBallot.Admin;
using ChisBallot.Admin.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChisBallot.Tests;

/// <summary>
/// The admin site's access rules, run against a real SQLite account store set
/// up the way the site sets it up: who counts as an admin, how invitations
/// work, and that a second factor is required.
/// </summary>
public sealed class AdminAccessTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"chisballot-test-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration["IdentityDatabasePath"] = this.databasePath;
        builder.Configuration["PublicUrl"] = "https://admin.example.test/";
        builder.Services.AddLogging();
        builder.AddAdminAuth();
        this.app = builder.Build();

        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await this.app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(this.databasePath);
    }

    private static string NewEmail() => $"admin-{Guid.NewGuid():N}@example.test";

    private static ClaimsPrincipal PrincipalFor(ApplicationUser user) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Role, AdminAuth.AdminRole)], "test"));

    private async Task<bool> PolicyAllowsAsync(AsyncServiceScope scope, ClaimsPrincipal principal)
    {
        IAuthorizationService authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(principal, resource: null, AdminAuth.PolicyName)).Succeeded;
    }

    [Fact]
    public async Task An_invitation_makes_an_admin_account_and_a_link_that_sets_the_password_once()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();
        string email = NewEmail();

        string link = await scope.ServiceProvider.GetRequiredService<AdminInvites>().InviteAsync(email);

        Assert.StartsWith("https://admin.example.test/Account/ResetPassword?code=", link);
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = (await users.FindByEmailAsync(email))!;
        Assert.True(await users.IsInRoleAsync(user, AdminAuth.AdminRole));
        Assert.False(await users.HasPasswordAsync(user));

        string token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(link[(link.IndexOf("code=", StringComparison.Ordinal) + 5)..]));
        Assert.False((await users.ResetPasswordAsync(user, token, "too short")).Succeeded);
        Assert.True((await users.ResetPasswordAsync(user, token, "a long enough password 1")).Succeeded);
        Assert.False((await users.ResetPasswordAsync(user, token, "another long password 2")).Succeeded);
    }

    [Fact]
    public async Task Something_that_is_not_an_email_address_is_refused()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<AdminInvites>().InviteAsync("not an email"));
    }

    [Fact]
    public async Task An_admin_without_a_second_factor_is_not_let_in_until_they_have_one()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();
        string email = NewEmail();
        await scope.ServiceProvider.GetRequiredService<AdminInvites>().InviteAsync(email);
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = (await users.FindByEmailAsync(email))!;

        Assert.False(await this.PolicyAllowsAsync(scope, PrincipalFor(user)));

        await users.SetTwoFactorEnabledAsync(user, true);

        Assert.True(await this.PolicyAllowsAsync(scope, PrincipalFor(user)));
    }

    [Fact]
    public async Task Someone_with_a_second_factor_who_is_not_an_admin_is_refused()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = new() { UserName = NewEmail(), Email = NewEmail(), TwoFactorEnabled = true };
        await users.CreateAsync(user);

        // Even a sign-in cookie that claims the admin role does not help: the role is read from the account store.
        Assert.False(await this.PolicyAllowsAsync(scope, PrincipalFor(user)));
    }

    [Fact]
    public async Task Removing_the_admin_role_takes_effect_at_once_even_for_an_existing_sign_in()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();
        string email = NewEmail();
        await scope.ServiceProvider.GetRequiredService<AdminInvites>().InviteAsync(email);
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = (await users.FindByEmailAsync(email))!;
        await users.SetTwoFactorEnabledAsync(user, true);
        ClaimsPrincipal signedIn = PrincipalFor(user);
        Assert.True(await this.PolicyAllowsAsync(scope, signedIn));

        await users.RemoveFromRoleAsync(user, AdminAuth.AdminRole);

        Assert.False(await this.PolicyAllowsAsync(scope, signedIn));
    }

    [Fact]
    public async Task Nobody_who_is_not_signed_in_is_let_in()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();

        Assert.False(await this.PolicyAllowsAsync(scope, new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        await using AsyncServiceScope scope = this.app.Services.CreateAsyncScope();
        string email = NewEmail();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = new() { UserName = email, Email = email };
        await users.CreateAsync(user, "a long enough password 1");

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await users.AccessFailedAsync(user);
        }

        Assert.True(await users.IsLockedOutAsync(user));
    }
}
