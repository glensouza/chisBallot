namespace ChisBallot.Data;

using System.Security.Cryptography;

/// <summary>
/// The code a voter keeps to find their ballot afterwards, like K7Q-4MX-92B.
/// Random, and unrelated to the voting code.
/// </summary>
public static class ReceiptCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate()
    {
        char[] characters = RandomNumberGenerator.GetItems<char>(Alphabet, 9);
        return string.Join('-', new string(characters, 0, 3), new string(characters, 3, 3), new string(characters, 6, 3));
    }
}
