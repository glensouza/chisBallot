namespace ChisBallot.Contracts;

/// <summary>Everything a ballot page needs. Status is 0 Draft, 1 Open, 2 Closed.</summary>
public sealed record ElectionView(
    Guid Id,
    string Slug,
    string Name,
    int Status,
    bool ShowLiveResults,
    bool ShuffleContests,
    bool ShuffleChoices,
    bool IsDemo,
    IReadOnlyList<ContestView> Contests,
    string? RevoteOfSlug = null,
    string? RevoteSlug = null,
    Guid? RevoteId = null);
