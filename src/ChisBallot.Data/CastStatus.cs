namespace ChisBallot.Data;

public enum CastStatus
{
    /// <summary>The ballot was recorded and the code consumed.</summary>
    Cast,

    /// <summary>Unknown, already used, for another election, or the election is not open. Deliberately not distinguished.</summary>
    CodeRefused,

    /// <summary>The choices do not fit the ballot. The code is not consumed.</summary>
    SelectionsRefused,
}
