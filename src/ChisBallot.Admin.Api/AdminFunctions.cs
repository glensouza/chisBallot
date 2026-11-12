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
public sealed class AdminFunctions(ElectionReader elections, ElectionAdmin admin, ResultsReader results)
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
        if (!IsAdmin(request))
        {
            return Forbidden();
        }

        return new OkObjectResult(await elections.ListAsync(cancellationToken));
    }

    [Function("CreateElection")]
    public async Task<IActionResult> CreateElection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "elections")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(request))
        {
            return Forbidden();
        }

        CreateElectionRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<CreateElectionRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            body = null;
        }

        if (body is null)
        {
            return new BadRequestObjectResult(new CreateElectionResponse(null, "That request was not understood."));
        }

        CreateElectionResponse response = await admin.CreateAsync(body, cancellationToken);
        return response.Id is null ? new BadRequestObjectResult(response) : new OkObjectResult(response);
    }

    [Function("GetElectionDetail")]
    public async Task<IActionResult> GetElectionDetail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "elections/{id:guid}")] HttpRequest request,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(request))
        {
            return Forbidden();
        }

        ElectionAdminView? view = await LoadAsync(id, cancellationToken);
        return view is null ? new NotFoundResult() : new OkObjectResult(view);
    }

    [Function("OpenElection")]
    public async Task<IActionResult> OpenElection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "elections/{id:guid}/open")] HttpRequest request,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(request))
        {
            return Forbidden();
        }

        return await admin.OpenAsync(id, cancellationToken)
            ? new OkResult()
            : new ConflictObjectResult("Only a draft that has slips can be opened.");
    }

    [Function("CloseElection")]
    public async Task<IActionResult> CloseElection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "elections/{id:guid}/close")] HttpRequest request,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(request))
        {
            return Forbidden();
        }

        return await admin.CloseAsync(id, cancellationToken)
            ? new OkResult()
            : new ConflictObjectResult("Only an open vote can be closed.");
    }

    private async Task<ElectionAdminView?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<ElectionSummary> all = await elections.ListAsync(cancellationToken);
        ElectionSummary? summary = all.FirstOrDefault(e => e.Id == id);
        if (summary is null)
        {
            return null;
        }

        ElectionView? election = await elections.FindBySlugAsync(summary.Slug, includeDrafts: true, cancellationToken);
        TurnoutView? turnout = election is null ? null : await elections.GetTurnoutAsync(id, cancellationToken);
        if (election is null || turnout is null)
        {
            return null;
        }

        return new ElectionAdminView(election, turnout, await results.GetAsync(election, cancellationToken));
    }

    private static bool IsAdmin(HttpRequest request) => AdminPrincipal.IsAdmin(AdminPrincipal.Read(request));

    private static ObjectResult Forbidden() => new("Admin role required.") { StatusCode = StatusCodes.Status403Forbidden };
}
