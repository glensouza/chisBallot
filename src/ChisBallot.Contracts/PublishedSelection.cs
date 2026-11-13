namespace ChisBallot.Contracts;

/// <summary>
/// One line of a published ballot: a printed choice, or a write-in name. A
/// ballot with no lines is a blank ballot. There is no time and no order of
/// voting anywhere in it.
/// </summary>
public sealed record PublishedSelection(Guid ContestId, Guid? ChoiceId, string? WriteIn);
