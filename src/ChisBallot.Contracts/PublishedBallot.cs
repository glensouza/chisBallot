namespace ChisBallot.Contracts;

public sealed record PublishedBallot(string Receipt, IReadOnlyList<PublishedSelection> Selections);
