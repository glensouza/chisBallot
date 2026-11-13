namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;

public sealed class CodeTests
{
    [Fact]
    public void A_voting_code_has_sixteen_characters_and_is_random()
    {
        string first = VotingCode.Generate();
        string second = VotingCode.Generate();

        Assert.Equal(19, first.Length);
        Assert.Equal(16, VotingCode.Normalize(first).Length);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("abcd-ef12-3456-789a", "ABCDEF123456789A")]
    [InlineData("O0IL 1111 2222 3333", "0011111122223333")]
    public void Typing_a_code_loosely_still_gives_the_same_hash(string typed, string normalized)
    {
        Assert.Equal(normalized, VotingCode.Normalize(typed));
        Assert.Equal(VotingCode.Hash(normalized), VotingCode.Hash(typed));
    }

    [Fact]
    public void A_receipt_code_fits_the_column_and_is_unrelated_to_the_voting_code()
    {
        string receipt = ReceiptCode.Generate();

        Assert.Equal(11, receipt.Length);
        Assert.DoesNotContain(VotingCode.Normalize(VotingCode.Generate()), receipt);
    }

    [Theory]
    [InlineData("  Pat   LEE ", "Pat LEE", "pat lee")]
    [InlineData("Mary-Jane O'Neil Jr.", "Mary-Jane O'Neil Jr.", "mary-jane o'neil jr.")]
    public void Write_in_names_are_trimmed_collapsed_and_case_folded(string typed, string display, string key)
    {
        Assert.True(WriteInName.TryNormalize(typed, out string actualDisplay, out string actualKey));
        Assert.Equal(display, actualDisplay);
        Assert.Equal(key, actualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<script>")]
    [InlineData("1234")]
    [InlineData("Pat\nLee; DROP TABLE Ballots")]
    public void Odd_write_in_names_are_refused(string typed)
    {
        Assert.False(WriteInName.TryNormalize(typed, out _, out _));
    }

    [Fact]
    public void A_write_in_name_over_sixty_characters_is_refused()
    {
        Assert.False(WriteInName.TryNormalize(new string('a', 61), out _, out _));
        Assert.True(WriteInName.TryNormalize(new string('a', 60), out _, out _));
    }
}
