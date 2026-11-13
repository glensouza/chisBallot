namespace ChisBallot.Vote.Server;

using ChisBallot.Data;

/// <summary>The two limits the public API enforces, per client address.</summary>
public sealed record VoteLimits(AttemptCounter WrongCodes, AttemptCounter DemoSlips);
