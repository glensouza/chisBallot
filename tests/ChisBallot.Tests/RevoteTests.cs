using ChisBallot.Admin;
using ChisBallot.Contracts;
using ChisBallot.Data;
using Serilog;

namespace ChisBallot.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class RevoteTests(DatabaseFixture database)
{
    private AdminViews Views() => new(
        new ElectionReader(database.AdminConnectionString),
        new ResultsReader(database.AdminConnectionString),
        new ElectionAdmin(database.AdminConnectionString, Log.Logger));

    private async Task<ElectionView> TieDemoAsync()
    {
        await DemoSeed.EnsureAsync(database.OwnerConnectionString);
        return (await new ElectionReader(database.AdminConnectionString).FindBySlugAsync("demo-tie", includeDrafts: true))!;
    }

    [Fact]
    public async Task A_tie_is_followed_by_a_draft_with_only_the_tied_choices_and_the_open_seats()
    {
        ElectionView tie = await TieDemoAsync();
        AdminViews views = this.Views();

        CreateElectionResponse response = await views.StartRevoteAsync(tie.Id);

        Assert.NotNull(response.Id);
        ElectionReader reader = new(database.AdminConnectionString);
        ElectionView revote = (await reader.FindBySlugAsync("demo-tie-revote", includeDrafts: true))!;
        Assert.Equal(0, revote.Status);
        Assert.True(revote.IsDemo);
        Assert.Equal("demo-tie", revote.RevoteOfSlug);
        ContestView contest = Assert.Single(revote.Contests);
        Assert.Equal(1, contest.Seats);
        Assert.Equal(1, contest.MaxSelections);
        Assert.False(contest.AllowWriteIn);
        Assert.Equal(["Ben", "Cy"], contest.Choices.Select(choice => choice.Label).Order());

        ElectionView parent = (await reader.FindBySlugAsync("demo-tie", includeDrafts: true))!;
        Assert.Equal("demo-tie-revote", parent.RevoteSlug);
        Assert.Equal(revote.Id, parent.RevoteId);

        // The public site does not show the revote until it is no longer a draft.
        ElectionView publicParent = (await new ElectionReader(database.VoteConnectionString).FindBySlugAsync("demo-tie", includeDrafts: false))!;
        Assert.Null(publicParent.RevoteSlug);
    }

    [Fact]
    public async Task A_vote_has_one_revote()
    {
        ElectionView tie = await TieDemoAsync();
        AdminViews views = this.Views();
        await views.StartRevoteAsync(tie.Id);

        CreateElectionResponse again = await views.StartRevoteAsync(tie.Id);

        Assert.Null(again.Id);
        Assert.Equal("This vote already has a revote.", again.Error);
    }

    [Fact]
    public async Task Only_a_closed_vote_with_a_tie_can_have_a_revote()
    {
        AdminViews views = this.Views();

        ElectionSeed open = await ElectionSeed.CreateAsync(database);
        Assert.Equal("Only a closed vote can have a revote.", (await views.StartRevoteAsync(open.ElectionId)).Error);

        await database.Caster.CastAsync(open.ElectionId, open.Codes[0], [new BallotSelection(open.ContestId, open.ChoiceIds[0])], []);
        await new ElectionAdmin(database.AdminConnectionString, Log.Logger).CloseAsync(open.ElectionId);
        Assert.Equal("No question in this vote ended in a tie.", (await views.StartRevoteAsync(open.ElectionId)).Error);
    }

    [Fact]
    public async Task Resetting_the_demos_also_removes_a_revote_made_from_them()
    {
        ElectionView tie = await TieDemoAsync();
        await this.Views().StartRevoteAsync(tie.Id);

        await DemoSeed.ResetAsync(database.OwnerConnectionString);

        ElectionReader reader = new(database.AdminConnectionString);
        Assert.Null(await reader.FindBySlugAsync("demo-tie-revote", includeDrafts: true));
        Assert.NotNull(await reader.FindBySlugAsync("demo-tie", includeDrafts: true));
    }
}
