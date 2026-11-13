namespace ChisBallot.Contracts;

public sealed record AdminIdentity(string Name, IReadOnlyList<string> Roles);
