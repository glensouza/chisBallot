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

        // Counts per choice. The database answers only for a Closed election, or an
        // Open one that turned live results on in Draft.
        api.MapGet("/elections/{slug}/results", async (string slug, ElectionReader elections, ResultsReader results, CancellationToken cancellationToken) =>
        {
            ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
            if (election is null)
            {
                return Results.NotFound();
            }

            IReadOnlyList<ContestResult>? contests = await results.GetAsync(election, cancellationToken);
            return contests is null ? Results.NotFound() : Results.Ok(contests);
        });

        // Every anonymous ballot, in receipt-code order. Closed elections only.
        api.MapGet("/elections/{slug}/ballots", async (string slug, ElectionReader elections, AuditReader audit, CancellationToken cancellationToken) =>
        {
            ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
            if (election is null)
            {
                return Results.NotFound();
            }

            IReadOnlyList<PublishedBallot>? ballots = await audit.GetBallotsAsync(election.Id, cancellationToken);
            return ballots is null ? Results.NotFound() : Results.Ok(ballots);
        });

        // The commit this build came from, set by the deploy workflow.
        api.MapGet("/version", () =>
        {
            string? informational = typeof(VoteEndpoints).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            int plus = informational?.IndexOf('+') ?? -1;
            return Results.Ok(new VersionView(plus < 0 ? null : informational![(plus + 1)..]));
        });

        // Only for a demo election that is open; the database refuses anything else.
        api.MapPost("/elections/{slug}/demo-slip", async (HttpContext http, string slug, ElectionReader elections, DemoSlips demoSlips, VoteLimits limits, CancellationToken cancellationToken) =>
        {
            if (!limits.DemoSlips.TryAdd(ClientAddress(http)))
            {
                return Results.Json(new CastResponse(null, "Too many requests. Wait a few minutes."), statusCode: StatusCodes.Status429TooManyRequests);
            }

            ElectionView? election = await elections.FindBySlugAsync(slug, includeDrafts: false, cancellationToken);
            if (election is null || !election.IsDemo)
            {
                return Results.NotFound();
            }

            string? code = await demoSlips.IssueAsync(election.Id, cancellationToken);
            return code is null ? Results.NotFound() : Results.Ok(new DemoSlipResponse(code));
        });

        api.MapPost("/cast", async (HttpContext http, BallotCaster caster, VoteLimits limits, CancellationToken cancellationToken) =>
        {
            HttpRequest httpRequest = http.Request;
            string client = ClientAddress(http);
            if (limits.WrongCodes.IsBlocked(client))
            {
                return Results.Json(new CastResponse(null, "Too many wrong codes. Wait a few minutes and try again."), statusCode: StatusCodes.Status429TooManyRequests);
            }

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
                CastStatus.CodeRefused => RefusedCode(limits, client),
                _ => Results.BadRequest(new CastResponse(null, "Those choices do not fit this ballot.")),
            };
        });
    }

    // A refused code counts against the client, so a wrong guess costs an attempt.
    private static IResult RefusedCode(VoteLimits limits, string client)
    {
        limits.WrongCodes.Add(client);
        return Results.Json(new CastResponse(null, "That code cannot be used."), statusCode: StatusCodes.Status403Forbidden);
    }

    // Cloudflare's tunnel connects from this machine, so the real address is in
    // CF-Connecting-IP. It is used only as an in-memory counting key and never logged.
    private static string ClientAddress(HttpContext http) =>
        http.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>The two limits the public API enforces, per client address.</summary>
public sealed record VoteLimits(AttemptCounter WrongCodes, AttemptCounter DemoSlips);
