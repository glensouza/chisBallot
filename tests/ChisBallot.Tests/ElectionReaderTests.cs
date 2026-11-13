namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;

[Collection(DatabaseCollection.Name)]
public sealed class ElectionReaderTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_open_election_is_found_by_its_slug_with_its_contests_and_choices()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, allowWriteIn: true, maxSelections: 2);
        ElectionReader reader = new(database.VoteConnectionString);

        ElectionView? election = await reader.FindBySlugAsync(seed.ElectionId.ToString("N"), includeDrafts: false);

        Assert.NotNull(election);
        Assert.Equal(seed.ElectionId, election.Id);
        ContestView contest = Assert.Single(election.Contests);
        Assert.Equal(2, contest.MaxSelections);
        Assert.True(contest.AllowWriteIn);
        Assert.Equal(seed.ChoiceIds.Order(), contest.Choices.Select(c => c.Id).Order());
    }

    [Fact]
    public async Task The_public_cannot_see_a_draft_but_admins_can()
    {
        ElectionSeed draft = await ElectionSeed.CreateAsync(database, open: false);
        string slug = draft.ElectionId.ToString("N");

        Assert.Null(await new ElectionReader(database.VoteConnectionString).FindBySlugAsync(slug, includeDrafts: false));
        Assert.NotNull(await new ElectionReader(database.AdminConnectionString).FindBySlugAsync(slug, includeDrafts: true));
        Assert.Contains(await new ElectionReader(database.AdminConnectionString).ListAsync(), e => e.Id == draft.ElectionId);
    }

    [Fact]
    public async Task Turnout_counts_used_slips_and_the_reader_never_exposes_ballots()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 4);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], [new BallotSelection(seed.ContestId, seed.ChoiceIds[0])], []);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[1], [new BallotSelection(seed.ContestId, seed.ChoiceIds[1])], []);

        TurnoutView? turnout = await new ElectionReader(database.VoteConnectionString).GetTurnoutAsync(seed.ElectionId);

        Assert.Equal(new TurnoutView(4, 2, 2), turnout);
    }

    [Fact]
    public async Task An_unknown_election_has_no_turnout()
    {
        Assert.Null(await new ElectionReader(database.VoteConnectionString).GetTurnoutAsync(Guid.NewGuid()));
    }
}
