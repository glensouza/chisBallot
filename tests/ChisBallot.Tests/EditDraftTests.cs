namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;
using Serilog;

[Collection(DatabaseCollection.Name)]
public sealed class EditDraftTests(DatabaseFixture database)
{
    private ElectionAdmin Admin() => new(database.AdminConnectionString, Log.Logger);

    private ElectionReader Reader() => new(database.AdminConnectionString);

    private static CreateElectionRequest Request(string name, string slug, params ContestInput[] contests) =>
        new(name, slug, ShowLiveResults: false, ShuffleContests: false, ShuffleChoices: false, contests);

    private static ContestInput Contest(string title, int seats, params string[] choices) =>
        new(title, seats, seats, AllowWriteIn: false, choices);

    private static string NewSlug() => "draft-" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task A_draft_can_be_changed_and_its_questions_are_replaced()
    {
        string slug = NewSlug();
        CreateElectionResponse made = await this.Admin().CreateAsync(Request("First name", slug, Contest("Old question", 1, "A", "B")));

        string newSlug = NewSlug();
        CreateElectionResponse changed = await this.Admin().UpdateDraftAsync(
            made.Id!.Value,
            Request("Second name", newSlug, Contest("Pick two", 2, "X", "Y", "Z"), Contest("Yes or no", 1, "Yes", "No")));

        Assert.Null(changed.Error);
        ElectionView election = (await this.Reader().FindBySlugAsync(newSlug, includeDrafts: true))!;
        Assert.Equal(made.Id, election.Id);
        Assert.Equal("Second name", election.Name);
        Assert.Equal(["Pick two", "Yes or no"], election.Contests.Select(c => c.Title));
        Assert.Equal(["X", "Y", "Z"], election.Contests[0].Choices.Select(c => c.Label));
        Assert.Null(await this.Reader().FindBySlugAsync(slug, includeDrafts: true));
    }

    [Fact]
    public async Task A_vote_that_is_open_cannot_be_changed()
    {
        ElectionSeed open = await ElectionSeed.CreateAsync(database);

        CreateElectionResponse response = await this.Admin().UpdateDraftAsync(open.ElectionId, Request("New name", NewSlug(), Contest("Q", 1, "A", "B")));

        Assert.Null(response.Id);
        Assert.Equal("Only a draft can be changed.", response.Error);
    }

    [Fact]
    public async Task The_address_stays_as_it_was_once_slips_have_been_made()
    {
        string slug = NewSlug();
        CreateElectionResponse made = await this.Admin().CreateAsync(Request("With slips", slug, Contest("Q", 1, "A", "B")));
        await this.Admin().IssueSlipsAsync(made.Id!.Value, 3);

        CreateElectionResponse changed = await this.Admin().UpdateDraftAsync(made.Id.Value, Request("Renamed", NewSlug(), Contest("Q2", 1, "C", "D")));

        Assert.Null(changed.Error);
        ElectionView election = (await this.Reader().FindBySlugAsync(slug, includeDrafts: true))!;
        Assert.Equal("Renamed", election.Name);
        Assert.Equal("Q2", election.Contests[0].Title);
    }

    [Fact]
    public async Task An_address_already_used_by_another_vote_is_refused()
    {
        string taken = NewSlug();
        await this.Admin().CreateAsync(Request("Other", taken, Contest("Q", 1, "A", "B")));
        CreateElectionResponse made = await this.Admin().CreateAsync(Request("Mine", NewSlug(), Contest("Q", 1, "A", "B")));

        CreateElectionResponse changed = await this.Admin().UpdateDraftAsync(made.Id!.Value, Request("Mine", taken, Contest("Q", 1, "A", "B")));

        Assert.Equal("That address is already used by another vote.", changed.Error);
    }

    [Fact]
    public async Task A_change_that_breaks_the_rules_is_refused_before_it_touches_the_draft()
    {
        string slug = NewSlug();
        CreateElectionResponse made = await this.Admin().CreateAsync(Request("Keeps its questions", slug, Contest("Original", 1, "A", "B")));

        CreateElectionResponse changed = await this.Admin().UpdateDraftAsync(made.Id!.Value, Request("", slug, Contest("Original", 1, "A", "B")));

        Assert.NotNull(changed.Error);
        ElectionView election = (await this.Reader().FindBySlugAsync(slug, includeDrafts: true))!;
        Assert.Equal("Keeps its questions", election.Name);
        Assert.Equal("Original", election.Contests[0].Title);
    }
}
