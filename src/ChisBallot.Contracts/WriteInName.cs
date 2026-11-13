using System.Text.RegularExpressions;

namespace ChisBallot.Contracts;

/// <summary>
/// Write-in names are limited and normalized so counting follows fixed rules
/// that nobody can bend by hand: trimmed, inner whitespace collapsed, and case
/// folded into a key that is what actually gets counted.
/// </summary>
public static partial class WriteInName
{
    public const int MaxLength = 60;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\p{L}[\p{L} .'\-]*$")]
    private static partial Regex Allowed();

    public static bool TryNormalize(string? name, out string display, out string key)
    {
        display = string.Empty;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string collapsed = Whitespace().Replace(name.Trim(), " ");
        if (collapsed.Length > MaxLength || !Allowed().IsMatch(collapsed))
        {
            return false;
        }

        display = collapsed;
        key = collapsed.ToLowerInvariant();
        return true;
    }
}
