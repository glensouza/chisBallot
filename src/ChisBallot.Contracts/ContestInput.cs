namespace ChisBallot.Contracts;

/// <summary>One question on a new election. Choices are the printed options, in order.</summary>
public sealed record ContestInput(string Title, int Seats, int MaxSelections, bool AllowWriteIn, IReadOnlyList<string> Choices);
