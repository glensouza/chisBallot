namespace ChisBallot.Contracts;

public sealed record ContestResult(Guid ContestId, string Title, int Seats, IReadOnlyList<ResultEntry> Entries, TieInfo? Tie);
