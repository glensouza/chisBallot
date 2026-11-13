namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;

[Collection(DatabaseCollection.Name)]
public sealed class AuditTests(DatabaseFixture database)
{
    [Fact]
    public async Task The_ballot_list_is_not_available_until_the_election_is_closed()
    {
        ElectionSeed open = await ElectionSeed.CreateAsync(database);
        await database.Caster.CastAsync(open.ElectionId, open.Codes[0], [new BallotSelection(open.ContestId, open.ChoiceIds[0])], []);

        Assert.Null(await new AuditReader(database.VoteConnectionString).GetBallotsAsync(open.ElectionId));
    }

    [Fact]
    public async Task A_closed_election_publishes_every_ballot_in_receipt_order_with_its_choices_and_write_ins()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 4, allowWriteIn: true, maxSelections: 2);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], [new BallotSelection(seed.ContestId, seed.ChoiceIds[0]), new BallotSelection(seed.ContestId, seed.ChoiceIds[1])], []);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[1], [], [new WriteInEntry(seed.ContestId, "Pat  Lee")]);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[2], [], []);
        await new ElectionAdmin(database.AdminConnectionString, Serilog.Log.Logger).CloseAsync(seed.ElectionId);

        IReadOnlyList<PublishedBallot>? ballots = await new AuditReader(database.VoteConnectionString).GetBallotsAsync(seed.ElectionId);

        Assert.NotNull(ballots);
        Assert.Equal(3, ballots.Count);
        Assert.Equal(ballots.Select(b => b.Receipt).Order(StringComparer.Ordinal), ballots.Select(b => b.Receipt));
        Assert.Contains(ballots, b => b.Selections.Count == 2 && b.Selections.All(s => s.ChoiceId is not null));
        Assert.Contains(ballots, b => b.Selections.Count == 1 && b.Selections[0].WriteIn == "Pat Lee");
        Assert.Contains(ballots, b => b.Selections.Count == 0);
    }

    [Fact]
    public async Task The_tie_demo_publishes_a_tie_that_the_recount_rules_agree_with()
    {
        await DemoSeed.EnsureAsync(database.OwnerConnectionString);
        ElectionReader reader = new(database.VoteConnectionString);
        ElectionView tie = (await reader.FindBySlugAsync("demo-tie", includeDrafts: false))!;

        IReadOnlyList<ContestResult>? published = await new ResultsReader(database.VoteConnectionString).GetAsync(tie);
        IReadOnlyList<PublishedBallot>? ballots = await new AuditReader(database.VoteConnectionString).GetBallotsAsync(tie.Id);

        Assert.NotNull(published);
        Assert.NotNull(ballots);
        Assert.Equal(13, ballots.Count);
        TieInfo info = published[0].Tie!;
        Assert.Equal(["Ben", "Cy"], info.TiedLabels.Order());
        Assert.Equal(1, info.OpenSeats);

        // The same counts, rebuilt from the published ballots alone.
        ContestView contest = tie.Contests[0];
        List<(string, int, bool)> counts = [.. contest.Choices.Select(choice => (choice.Label, ballots.Count(b => b.Selections.Any(s => s.ChoiceId == choice.Id)), false))];
        ContestResult recount = ResultsCalculator.Rank(contest.Id, contest.Title, contest.Seats, counts);
        Assert.Equal(published[0].Entries.Select(e => (e.Label, e.Votes)), recount.Entries.Select(e => (e.Label, e.Votes)));
    }
}
