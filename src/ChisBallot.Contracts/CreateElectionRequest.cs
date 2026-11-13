namespace ChisBallot.Contracts;

public sealed record CreateElectionRequest(
    string Name,
    string Slug,
    bool ShowLiveResults,
    bool ShuffleContests,
    bool ShuffleChoices,
    IReadOnlyList<ContestInput> Contests);
