namespace ChisBallot.Contracts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// A SHA-256 fingerprint of the published ballot list. It is printed on the signed
/// record and shown on the audit page, so anyone can check that the record matches the
/// ballots that were published. It depends only on the receipt codes and the choices,
/// never on the order the server happens to return them in.
/// </summary>
public static class BallotFingerprint
{
    /// <summary>
    /// One line per ballot in receipt-code order: the receipt, a colon, then the ballot's
    /// lines (contest id, a slash, then the choice id or "w:" and the write-in name), sorted
    /// and separated by commas. Ids are 32 hex digits. The text is UTF-8, lines end with a
    /// line feed, and the fingerprint is the lowercase hex SHA-256 of it.
    /// </summary>
    public static string Compute(IEnumerable<PublishedBallot> ballots)
    {
        StringBuilder text = new();
        foreach (PublishedBallot ballot in ballots.OrderBy(b => b.Receipt, StringComparer.Ordinal))
        {
            IEnumerable<string> lines = ballot.Selections.Select(Describe).Order(StringComparer.Ordinal);
            text.Append(ballot.Receipt).Append(':').Append(string.Join(',', lines)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string Describe(PublishedSelection selection) =>
        selection.ContestId.ToString("N") + "/" + (selection.ChoiceId is Guid choice ? choice.ToString("N") : "w:" + selection.WriteIn);
}
