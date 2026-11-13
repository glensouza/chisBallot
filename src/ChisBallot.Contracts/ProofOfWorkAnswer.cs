namespace ChisBallot.Contracts;

/// <summary>A challenge as it was handed out, with the number that solves it.</summary>
public sealed record ProofOfWorkAnswer(string Nonce, long Expires, int Difficulty, string Signature, long Counter);
