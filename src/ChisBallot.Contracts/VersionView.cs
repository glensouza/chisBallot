namespace ChisBallot.Contracts;

/// <summary>What is running: the commit the site was built from, when known.</summary>
public sealed record VersionView(string? Commit);
