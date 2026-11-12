using ChisBallot.Contracts;
using ChisBallot.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace ChisBallot.Admin.Api;

/// <summary>
/// The admin API. Every function requires the admin role. It connects as the
/// chisballot_admin database user, which cannot read ballots.
/// </summary>
public sealed class AdminFunctions(ElectionReader elections)
{
    [Function("GetMe")]
    public IActionResult GetMe([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest request)
    {
        AdminIdentity? identity = AdminPrincipal.Read(request);
        return AdminPrincipal.IsAdmin(identity) ? new OkObjectResult(identity) : new ForbidResult();
    }

    [Function("ListElections")]
    public async Task<IActionResult> ListElections(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "elections")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!AdminPrincipal.IsAdmin(AdminPrincipal.Read(request)))
        {
            return new ObjectResult("Admin role required.") { StatusCode = StatusCodes.Status403Forbidden };
        }

        return new OkObjectResult(await elections.ListAsync(cancellationToken));
    }
}
