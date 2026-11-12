using ChisBallot.Contracts;
using ChisBallot.Data;

namespace ChisBallot.Admin;

/// <summary>Puts together what the vote page shows: the setup, turnout and, when allowed, results.</summary>
public sealed class AdminViews(ElectionReader elections, ResultsReader results)
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
}
