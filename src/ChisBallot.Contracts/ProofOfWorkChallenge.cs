namespace ChisBallot.Contracts;

/// <summary>
/// A small puzzle the voting site hands out before a ballot or a demo slip is accepted:
/// find a number that, joined to the nonce, hashes to a value starting with Difficulty zero
/// bits. The signature (made by the site, with a key only it knows) lets it recognise its
/// own challenge later without remembering it.
/// </summary>
public sealed record ProofOfWorkChallenge(string Nonce, long Expires, int Difficulty, string Signature);
