using System.Security.Cryptography;
using System.Text;

namespace ChisBallot.Data;

/// <summary>
/// The code printed on a slip. Sixteen characters from an alphabet without
/// look-alike letters, drawn from the system's cryptographic random number
/// generator (80 bits). Only its SHA-256 hash is ever stored.
/// </summary>
public static class VotingCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 16;

    public static string Generate()
    {
        char[] characters = RandomNumberGenerator.GetItems<char>(Alphabet, Length);
        return string.Join('-', new string(characters, 0, 4), new string(characters, 4, 4), new string(characters, 8, 4), new string(characters, 12, 4));
    }

    /// <summary>Removes dashes and spaces, upper-cases, and maps look-alikes (O to 0, I and L to 1).</summary>
    public static string Normalize(string code)
    {
        StringBuilder builder = new();
        foreach (char character in code)
        {
            if (character is '-' or ' ')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                char other => other,
            });
        }

        return builder.ToString();
    }

    public static byte[] Hash(string code)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));
    }
}
