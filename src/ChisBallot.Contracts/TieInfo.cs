namespace ChisBallot.Contracts;

/// <summary>
/// A tie that decides who wins. Choices clearly ahead keep their seats; the tied
/// choices are listed with the number of seats still open, and go to a revote.
/// </summary>
public sealed record TieInfo(IReadOnlyList<string> TiedLabels, int OpenSeats);
