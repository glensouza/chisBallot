using ChisBallot.Contracts;
using ChisBallot.Data;
using System.Text.Json;

namespace ChisBallot.Vote.Server;

/// <summary>
/// The public voting API. It exposes a ballot, turnout, and casting a
/// ballot, and nothing else: no admin endpoints, and no way to read results
/// (that comes later and is gated by the database).
/// </summary>
public static class VoteEndpoints
{
    public static void MapVoteEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/elections/{slug}", async (string slug, ElectionReader elections, CancellationToken cancellationToken) =>
        {
            ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
            return election is null ? Results.NotFound() : Results.Ok(election);
        });

        api.MapGet("/elections/{slug}/turnout", async (string slug, ElectionReader elections, CancellationToken cancellationToken) =>
        {
            ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
            if (election is null)
            {
                return Results.NotFound();
            }

            TurnoutView? turnout = await elections.GetTurnoutAsync(election.Id, cancellationToken);
            return turnout is null ? Results.NotFound() : Results.Ok(turnout);
        });

        // Only for a demo election that is open; the database refuses anything else.
        api.MapPost("/elections/{slug}/demo-slip", async (string slug, ElectionReader elections, DemoSlips demoSlips, CancellationToken cancellationToken) =>
        {
            ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
            if (election is null || !election.IsDemo)
            {
                return Results.NotFound();
            }

            string? code = await demoSlips.IssueAsync(election.Id, cancellationToken);
            return code is null ? Results.NotFound() : Results.Ok(new DemoSlipResponse(code));
        });

        api.MapPost("/cast", async (HttpRequest httpRequest, BallotCaster caster, CancellationToken cancellationToken) =>
        {
            CastRequest? request;
            try
            {
                request = await httpRequest.ReadFromJsonAsync<CastRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Code) || request.Selections is null || request.WriteIns is null)
            {
                return Results.BadRequest(new CastResponse(null, "That request was not understood."));
            }

            CastOutcome outcome = await caster.CastAsync(
                request.ElectionId,
                request.Code,
                [.. request.Selections.Select(s => new BallotSelection(s.ContestId, s.ChoiceId))],
                [.. request.WriteIns.Select(w => new WriteInEntry(w.ContestId, w.Name))],
                cancellationToken);

            return outcome.Status switch
            {
                CastStatus.Cast => Results.Ok(new CastResponse(outcome.ReceiptCode, null)),
                CastStatus.CodeRefused => Results.Json(new CastResponse(null, "That code cannot be used."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.BadRequest(new CastResponse(null, "Those choices do not fit this ballot.")),
            };
        });
    }
}
