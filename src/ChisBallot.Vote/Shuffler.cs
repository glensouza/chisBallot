namespace ChisBallot.Vote;

using System.Security.Cryptography;

/// <summary>
/// Fisher-Yates shuffle driven by the browser's cryptographic random number
/// generator. The order it produces is never stored, sent or logged.
/// </summary>
public static class Shuffler
{
    public static void Shuffle<T>(IList<T> items)
    {
        for (int index = items.Count - 1; index > 0; index--)
        {
            int other = RandomNumberGenerator.GetInt32(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }

    public static IReadOnlyList<T> Shuffled<T>(IReadOnlyList<T> items)
    {
        List<T> copy = [.. items];
        Shuffle(copy);
        return copy;
    }
}
