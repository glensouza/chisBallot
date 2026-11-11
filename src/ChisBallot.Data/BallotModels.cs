namespace ChisBallot.Data;

public sealed record BallotSelection(Guid ContestId, Guid ChoiceId);

public sealed record WriteInEntry(Guid ContestId, string Name);

public enum CastStatus
{
    /// <summary>The ballot was recorded and the code consumed.</summary>
    Cast,

    /// <summary>Unknown, already used, for another election, or the election is not open. Deliberately not distinguished.</summary>
    CodeRefused,

    /// <summary>The choices do not fit the ballot. The code is not consumed.</summary>
    SelectionsRefused,
}

public sealed record CastOutcome(CastStatus Status, string? ReceiptCode);
