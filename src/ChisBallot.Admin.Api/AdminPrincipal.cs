using System.Text.Json;
using ChisBallot.Contracts;
using Microsoft.AspNetCore.Http;

namespace ChisBallot.Admin.Api;

/// <summary>
/// Reads the signed-in user that Azure Static Web Apps puts in the
/// x-ms-client-principal header. The route rules already require the admin
/// role; every function checks it again so the API is safe on its own.
/// </summary>
public static class AdminPrincipal
{
    public const string AdminRole = "admin";

    private const string HeaderName = "x-ms-client-principal";

    public static AdminIdentity? Read(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues header) || header.Count == 0)
        {
#if DEBUG
            // Local development only. This block is compiled out of Release
            // builds, which are the only ones that are deployed.
            if (Environment.GetEnvironmentVariable("CHISBALLOT_DEV_ADMIN") == "1")
            {
                return new AdminIdentity("Local developer", [AdminRole, "authenticated"]);
            }
#endif
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(header[0]!));
            JsonElement root = document.RootElement;
            string name = root.TryGetProperty("userDetails", out JsonElement details) ? details.GetString() ?? string.Empty : string.Empty;
            List<string> roles = [];
            if (root.TryGetProperty("userRoles", out JsonElement userRoles) && userRoles.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement role in userRoles.EnumerateArray())
                {
                    if (role.GetString() is string value)
                    {
                        roles.Add(value);
                    }
                }
            }

            return new AdminIdentity(name, roles);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    public static bool IsAdmin(AdminIdentity? identity)
    {
        return identity is not null && identity.Roles.Contains(AdminRole);
    }
}
