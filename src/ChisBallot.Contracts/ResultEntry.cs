namespace ChisBallot.Contracts;

/// <summary>One entry in a contest's result: a printed choice or a write-in name.</summary>
public sealed record ResultEntry(string Label, int Votes, bool IsWriteIn, bool IsWinner);
