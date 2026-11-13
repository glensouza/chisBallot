namespace ChisBallot.Contracts;

public sealed record ContestView(Guid Id, string Title, int Seats, int MaxSelections, bool AllowWriteIn, IReadOnlyList<ChoiceView> Choices);
