namespace ChisBallot.Contracts;

/// <summary>
/// Turns vote counts into winners, following the rules in docs/data-model.md.
/// The top Seats choices win. A tie that decides who wins is never broken: the
/// contest is marked as a tie, choices clearly ahead keep their seats, and the
/// tied choices are listed with the seats still open for a revote.
/// This is a pure function of the counts, so the audit page can run the same
/// rules in a voter's browser.
/// </summary>
public static class ResultsCalculator
{
    public static ContestResult Rank(Guid contestId, string title, int seats, IEnumerable<(string Label, int Votes, bool IsWriteIn)> counts)
    {
        // Most votes first. Ties are listed alphabetically only so the page is stable.
        List<(string Label, int Votes, bool IsWriteIn)> ordered = counts
            .OrderByDescending(entry => entry.Votes)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Everyone fits: nobody is left out, so no tie can decide anything.
        if (ordered.Count <= seats)
        {
            return Build(contestId, title, seats, ordered, winners: ordered.Count, tie: null);
        }

        int lastWinning = ordered[seats - 1].Votes;
        int firstLeftOut = ordered[seats].Votes;

        if (firstLeftOut < lastWinning)
        {
            return Build(contestId, title, seats, ordered, winners: seats, tie: null);
        }

        // The last seat and the first place outside it are level. Everyone
        // strictly ahead keeps a seat; everyone on that level goes to a revote.
        int clearWinners = ordered.Count(entry => entry.Votes > lastWinning);
        List<string> tied = ordered.Where(entry => entry.Votes == lastWinning).Select(entry => entry.Label).ToList();
        return Build(contestId, title, seats, ordered, clearWinners, new TieInfo(tied, seats - clearWinners));
    }

    private static ContestResult Build(
        Guid contestId,
        string title,
        int seats,
        List<(string Label, int Votes, bool IsWriteIn)> ordered,
        int winners,
        TieInfo? tie)
    {
        List<ResultEntry> entries = ordered
            .Select((entry, index) => new ResultEntry(entry.Label, entry.Votes, entry.IsWriteIn, index < winners))
            .ToList();
        return new ContestResult(contestId, title, seats, entries, tie);
    }
}
