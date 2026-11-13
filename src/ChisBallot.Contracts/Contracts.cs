namespace ChisBallot.Contracts;

public sealed record ChoiceView(Guid Id, string Label);

public sealed record ContestView(Guid Id, string Title, int Seats, int MaxSelections, bool AllowWriteIn, IReadOnlyList<ChoiceView> Choices);

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
    IReadOnlyList<ContestView> Contests);

public sealed record ElectionSummary(Guid Id, string Slug, string Name, int Status, bool IsDemo, int CodesIssued);

/// <summary>Turnout is public: how many slips were used, never what was voted.</summary>
public sealed record TurnoutView(int CodesIssued, int CodesUsed, int BallotsCounted);

public sealed record SelectionDto(Guid ContestId, Guid ChoiceId);

public sealed record WriteInDto(Guid ContestId, string Name);

public sealed record CastRequest(Guid ElectionId, string Code, IReadOnlyList<SelectionDto> Selections, IReadOnlyList<WriteInDto> WriteIns);

public sealed record CastResponse(string? ReceiptCode, string? Error);

public sealed record AdminIdentity(string Name, IReadOnlyList<string> Roles);

public sealed record DemoSlipResponse(string Code);
