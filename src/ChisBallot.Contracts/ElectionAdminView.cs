namespace ChisBallot.Contracts;

/// <summary>What the admin sees for one election. Results are null until the database allows them.</summary>
public sealed record ElectionAdminView(ElectionView Election, TurnoutView Turnout, IReadOnlyList<ContestResult>? Results);
