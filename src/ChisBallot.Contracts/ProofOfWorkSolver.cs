namespace ChisBallot.Contracts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Solves and checks the proof of work. The browser app solves it, the server checks it, and
/// both use this one class so they cannot disagree. It is a cost to a script that sends many
/// requests, not a test that a person is present.
/// </summary>
public static class ProofOfWorkSolver
{
    // How many hashes to try before letting the browser draw the screen.
    private const int YieldEvery = 4096;

    public static bool Check(string nonce, long counter, int difficulty)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{nonce}:{counter}"));
        return LeadingZeroBits(hash) >= difficulty;
    }

    public static async Task<long> SolveAsync(string nonce, int difficulty, CancellationToken cancellationToken = default)
    {
        for (long counter = 0; ; counter++)
        {
            if (counter % YieldEvery == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (Check(nonce, counter, difficulty))
            {
                return counter;
            }
        }
    }

    private static int LeadingZeroBits(byte[] hash)
    {
        int bits = 0;
        foreach (byte value in hash)
        {
            if (value == 0)
            {
                bits += 8;
                continue;
            }

            bits += System.Numerics.BitOperations.LeadingZeroCount((uint)value) - 24;
            break;
        }

        return bits;
    }
}
