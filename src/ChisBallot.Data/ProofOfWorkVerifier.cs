namespace ChisBallot.Data;

using System.Security.Cryptography;
using System.Text;
using ChisBallot.Contracts;

/// <summary>
/// Hands out proof-of-work challenges and checks the answers. A challenge is signed with a key
/// made when the site starts, so the site needs to remember nothing about the ones it handed out,
/// only the ones already answered (so each can be used once), and only until they expire. A
/// restart simply makes old challenges invalid. Nothing here is written to disk or logged.
/// </summary>
public sealed class ProofOfWorkVerifier(int difficulty, TimeSpan lifetime, TimeProvider? time = null)
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly Dictionary<string, long> answered = [];
    private readonly Lock gate = new();

    public int Difficulty => difficulty;

    public ProofOfWorkChallenge Create()
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        long expires = this.clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds();
        return new ProofOfWorkChallenge(nonce, expires, difficulty, this.Sign(nonce, expires, difficulty));
    }

    /// <summary>True for a challenge this site made, not yet expired, solved, and not used before.</summary>
    public bool Verify(ProofOfWorkAnswer? answer)
    {
        if (answer is null || answer.Difficulty != difficulty || string.IsNullOrEmpty(answer.Nonce) || string.IsNullOrEmpty(answer.Signature))
        {
            return false;
        }

        long now = this.clock.GetUtcNow().ToUnixTimeSeconds();
        if (answer.Expires < now)
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(this.Sign(answer.Nonce, answer.Expires, answer.Difficulty));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(answer.Signature)))
        {
            return false;
        }

        if (!ProofOfWorkSolver.Check(answer.Nonce, answer.Counter, difficulty))
        {
            return false;
        }

        lock (this.gate)
        {
            foreach (string stale in this.answered.Where(pair => pair.Value < now).Select(pair => pair.Key).ToList())
            {
                this.answered.Remove(stale);
            }

            return this.answered.TryAdd(answer.Nonce, answer.Expires);
        }
    }

    private string Sign(string nonce, long expires, int level)
    {
        byte[] mac = HMACSHA256.HashData(this.key, Encoding.UTF8.GetBytes($"{nonce}|{expires}|{level}"));
        return Convert.ToBase64String(mac);
    }
}
