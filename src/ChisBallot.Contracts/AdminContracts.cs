namespace ChisBallot.Contracts;

/// <summary>One question on a new election. Choices are the printed options, in order.</summary>
public sealed record ContestInput(string Title, int Seats, int MaxSelections, bool AllowWriteIn, IReadOnlyList<string> Choices);

public sealed record CreateElectionRequest(
    string Name,
    string Slug,
    bool ShowLiveResults,
    bool ShuffleContests,
    bool ShuffleChoices,
    IReadOnlyList<ContestInput> Contests);

public sealed record CreateElectionResponse(Guid? Id, string? Error);

/// <summary>One entry in a contest's result: a printed choice or a write-in name.</summary>
public sealed record ResultEntry(string Label, int Votes, bool IsWriteIn, bool IsWinner);

/// <summary>
/// A tie that decides who wins. Choices clearly ahead keep their seats; the tied
/// choices are listed with the number of seats still open, and go to a revote.
/// </summary>
public sealed record TieInfo(IReadOnlyList<string> TiedLabels, int OpenSeats);

public sealed record ContestResult(Guid ContestId, string Title, int Seats, IReadOnlyList<ResultEntry> Entries, TieInfo? Tie);

/// <summary>What the admin sees for one election. Results are null until the database allows them.</summary>
public sealed record ElectionAdminView(ElectionView Election, TurnoutView Turnout, IReadOnlyList<ContestResult>? Results);
