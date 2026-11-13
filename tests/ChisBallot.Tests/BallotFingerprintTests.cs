namespace ChisBallot.Tests;

using ChisBallot.Contracts;

public sealed class BallotFingerprintTests
{
    private static readonly Guid Contest = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChoiceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChoiceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static PublishedBallot Ballot(string receipt, params PublishedSelection[] lines) => new(receipt, lines);

    [Fact]
    public void The_same_ballots_give_the_same_fingerprint_whatever_order_they_arrive_in()
    {
        PublishedBallot first = Ballot("AAA-111-111", new PublishedSelection(Contest, ChoiceA, null), new PublishedSelection(Contest, ChoiceB, null));
        PublishedBallot second = Ballot("BBB-222-222", new PublishedSelection(Contest, null, "Pat Lee"));
        PublishedBallot reordered = Ballot("AAA-111-111", new PublishedSelection(Contest, ChoiceB, null), new PublishedSelection(Contest, ChoiceA, null));

        Assert.Equal(BallotFingerprint.Compute([first, second]), BallotFingerprint.Compute([second, reordered]));
    }

    [Fact]
    public void Changing_a_choice_a_write_in_or_a_receipt_changes_the_fingerprint()
    {
        string baseline = BallotFingerprint.Compute([Ballot("AAA-111-111", new PublishedSelection(Contest, ChoiceA, null))]);

        Assert.NotEqual(baseline, BallotFingerprint.Compute([Ballot("AAA-111-111", new PublishedSelection(Contest, ChoiceB, null))]));
        Assert.NotEqual(baseline, BallotFingerprint.Compute([Ballot("AAA-111-111", new PublishedSelection(Contest, null, "Pat Lee"))]));
        Assert.NotEqual(baseline, BallotFingerprint.Compute([Ballot("AAA-111-112", new PublishedSelection(Contest, ChoiceA, null))]));
        Assert.NotEqual(baseline, BallotFingerprint.Compute([]));
    }

    [Fact]
    public void The_fingerprint_is_sixty_four_lowercase_hex_digits()
    {
        string fingerprint = BallotFingerprint.Compute([Ballot("AAA-111-111")]);

        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }
}
