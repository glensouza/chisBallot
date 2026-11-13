namespace ChisBallot.Contracts;

public sealed record ElectionSummary(Guid Id, string Slug, string Name, int Status, bool IsDemo, int CodesIssued);
