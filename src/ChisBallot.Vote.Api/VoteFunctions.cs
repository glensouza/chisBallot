using ChisBallot.Contracts;
using ChisBallot.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace ChisBallot.Vote.Api;

/// <summary>
/// The public voting API. It exposes a ballot, turnout, and casting a
/// ballot, and nothing else: no admin endpoints, and no way to read results
/// (that comes later and is gated by the database).
/// </summary>
public sealed class VoteFunctions(ElectionReader elections, BallotCaster caster)
{
    [Function("GetElection")]
    public async Task<IActionResult> GetElection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "elections/{slug}")] HttpRequest request,
        string slug,
        CancellationToken cancellationToken)
    {
        ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
        return election is null ? new NotFoundResult() : new OkObjectResult(election);
    }

    [Function("GetTurnout")]
    public async Task<IActionResult> GetTurnout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "elections/{slug}/turnout")] HttpRequest request,
        string slug,
        CancellationToken cancellationToken)
    {
        ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
        if (election is null)
        {
            return new NotFoundResult();
        }

        TurnoutView? turnout = await elections.GetTurnoutAsync(election.Id, cancellationToken);
        return turnout is null ? new NotFoundResult() : new OkObjectResult(turnout);
    }

    [Function("CastBallot")]
    public async Task<IActionResult> CastBallot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cast")] HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        CastRequest? request;
        try
        {
            request = await httpRequest.ReadFromJsonAsync<CastRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Code) || request.Selections is null || request.WriteIns is null)
        {
            return new BadRequestObjectResult(new CastResponse(null, "That request was not understood."));
        }

        CastOutcome outcome = await caster.CastAsync(
            request.ElectionId,
            request.Code,
            [.. request.Selections.Select(s => new BallotSelection(s.ContestId, s.ChoiceId))],
            [.. request.WriteIns.Select(w => new WriteInEntry(w.ContestId, w.Name))],
            cancellationToken);

        return outcome.Status switch
        {
            CastStatus.Cast => new OkObjectResult(new CastResponse(outcome.ReceiptCode, null)),
            CastStatus.CodeRefused => new ObjectResult(new CastResponse(null, "That code cannot be used.")) { StatusCode = StatusCodes.Status403Forbidden },
            _ => new BadRequestObjectResult(new CastResponse(null, "Those choices do not fit this ballot.")),
        };
    }
}
