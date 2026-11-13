using ChisBallot.Contracts;
using ChisBallot.Data;

namespace ChisBallot.Admin;

/// <summary>Puts together what the vote page shows: the setup, turnout and, when allowed, results.</summary>
public sealed class AdminViews(ElectionReader elections, ResultsReader results, ElectionAdmin admin)
{
    public async Task<ElectionAdminView?> LoadAsync(Guid id, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Makes the draft that follows a closed vote with ties: only the tied questions, with
    /// only the tied choices, for the seats still open. The site never breaks a tie; this is
    /// how it is settled. The revote needs its own slips, made the usual way.
    /// </summary>
    public async Task<CreateElectionResponse> StartRevoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ElectionAdminView? view = await this.LoadAsync(id, cancellationToken);
        if (view is null || view.Election.Status != 2 || view.Results is null)
        {
            return new CreateElectionResponse(null, "Only a closed vote can have a revote.");
        }

        if (view.Election.RevoteSlug is not null)
        {
            return new CreateElectionResponse(null, "This vote already has a revote.");
        }

        List<ContestInput> contests =
        [
            .. view.Results.Where(result => result.Tie is not null).Select(result => new ContestInput(
                result.Title,
                result.Tie!.OpenSeats,
                result.Tie.OpenSeats,
                AllowWriteIn: false,
                result.Tie.TiedLabels)),
        ];
        if (contests.Count == 0)
        {
            return new CreateElectionResponse(null, "No question in this vote ended in a tie.");
        }

        string name = $"{view.Election.Name} (revote)";
        if (name.Length > ElectionRequestValidator.MaxNameLength)
        {
            name = name[..ElectionRequestValidator.MaxNameLength];
        }

        string stem = view.Election.Slug.Length > ElectionRequestValidator.MaxSlugLength - 10
            ? view.Election.Slug[..(ElectionRequestValidator.MaxSlugLength - 10)].TrimEnd('-')
            : view.Election.Slug;
        CreateElectionResponse response = new(null, "That address is already used by another vote.");
        for (int attempt = 1; attempt <= 5 && response.Id is null && response.Error is not null && response.Error.StartsWith("That address", StringComparison.Ordinal); attempt++)
        {
            string slug = attempt == 1 ? $"{stem}-revote" : $"{stem}-revote-{attempt}";
            response = await admin.CreateAsync(
                new CreateElectionRequest(name, slug, ShowLiveResults: false, view.Election.ShuffleContests, view.Election.ShuffleChoices, contests),
                revoteOf: id,
                isDemo: view.Election.IsDemo,
                cancellationToken: cancellationToken);
        }

        return response;
    }
}
