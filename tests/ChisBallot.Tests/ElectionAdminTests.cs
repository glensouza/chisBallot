using ChisBallot.Contracts;
using ChisBallot.Data;
using Microsoft.Data.SqlClient;
using Serilog;

namespace ChisBallot.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ElectionAdminTests(DatabaseFixture database)
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private ElectionAdmin Admin() => new(database.AdminConnectionString, Logger);

    private static CreateElectionRequest Request(string slug) => new(
        "Board election",
        slug,
        ShowLiveResults: false,
        ShuffleContests: true,
        ShuffleChoices: true,
        [
            new ContestInput("Pick three", 3, 3, true, ["A", "B", "C", "D", "E"]),
            new ContestInput("Longer lunch?", 1, 1, false, ["Yes", "No"]),
        ]);

    private static string NewSlug() => "t-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task A_draft_election_is_created_with_its_questions_in_order()
    {
        string slug = NewSlug();

        CreateElectionResponse response = await Admin().CreateAsync(Request(slug));

        Assert.NotNull(response.Id);
        ElectionView? election = await new ElectionReader(database.AdminConnectionString).FindBySlugAsync(slug, includeDrafts: true);
        Assert.NotNull(election);
        Assert.Equal(0, election.Status);
        Assert.False(election.IsDemo);
        Assert.True(election.ShuffleContests);
        Assert.Equal(["Pick three", "Longer lunch?"], election.Contests.Select(c => c.Title).ToArray());
        Assert.Equal(["A", "B", "C", "D", "E"], election.Contests[0].Choices.Select(c => c.Label).ToArray());
        Assert.Equal(3, election.Contests[0].Seats);
        Assert.True(election.Contests[0].AllowWriteIn);
    }

    [Fact]
    public async Task A_bad_request_or_a_used_address_is_refused_with_a_reason()
    {
        string slug = NewSlug();
        await Admin().CreateAsync(Request(slug));

        CreateElectionResponse used = await Admin().CreateAsync(Request(slug));
        CreateElectionResponse bad = await Admin().CreateAsync(Request("Not A Slug"));

        Assert.Null(used.Id);
        Assert.Contains("already used", used.Error);
        Assert.Null(bad.Id);
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public async Task A_failed_create_leaves_nothing_behind()
    {
        string slug = NewSlug();
        await Admin().CreateAsync(Request(slug));
        int before = await CountAsync("SELECT COUNT(*) FROM dbo.Contests");

        await Admin().CreateAsync(Request(slug));

        Assert.Equal(before, await CountAsync("SELECT COUNT(*) FROM dbo.Contests"));
    }

    [Fact]
    public async Task A_draft_without_slips_cannot_be_opened()
    {
        CreateElectionResponse created = await Admin().CreateAsync(Request(NewSlug()));

        Assert.False(await Admin().OpenAsync(created.Id!.Value));
    }

    [Fact]
    public async Task An_election_with_slips_opens_once_and_then_closes_once()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, open: false);

        Assert.True(await Admin().OpenAsync(seed.ElectionId));
        Assert.False(await Admin().OpenAsync(seed.ElectionId));
        Assert.True(await Admin().CloseAsync(seed.ElectionId));
        Assert.False(await Admin().CloseAsync(seed.ElectionId));
        Assert.False(await Admin().OpenAsync(seed.ElectionId));
    }

    [Fact]
    public async Task A_draft_cannot_be_closed()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, open: false);

        Assert.False(await Admin().CloseAsync(seed.ElectionId));
    }

    [Fact]
    public async Task Results_come_back_only_after_close_and_follow_the_tie_rule()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 4, maxSelections: 1);
        ElectionReader reader = new(database.AdminConnectionString);
        ResultsReader results = new(database.AdminConnectionString);
        ElectionView election = (await reader.FindBySlugAsync(seed.ElectionId.ToString("N"), includeDrafts: true))!;

        // Two votes for choice 0, two for choice 1: level for the single seat.
        for (int index = 0; index < 4; index++)
        {
            await database.Caster.CastAsync(seed.ElectionId, seed.Codes[index], [new BallotSelection(seed.ContestId, seed.ChoiceIds[index % 2])], []);
        }

        Assert.Null(await results.GetAsync(election));

        await Admin().CloseAsync(seed.ElectionId);

        IReadOnlyList<ContestResult>? closed = await results.GetAsync(election);
        ContestResult contest = Assert.Single(closed!);
        Assert.NotNull(contest.Tie);
        Assert.Equal(2, contest.Tie.TiedLabels.Count);
        Assert.Equal(1, contest.Tie.OpenSeats);
        Assert.Equal(4, contest.Entries.Sum(e => e.Votes));
    }

    [Fact]
    public async Task Slips_are_made_once_and_only_their_hashes_are_stored()
    {
        CreateElectionResponse created = await Admin().CreateAsync(Request(NewSlug()));
        Guid id = created.Id!.Value;

        IReadOnlyList<string>? codes = await Admin().IssueSlipsAsync(id, 5);

        Assert.NotNull(codes);
        Assert.Equal(5, codes.Count);
        Assert.Equal(5, codes.Distinct().Count());
        Assert.Equal(5, await CountAsync($"SELECT COUNT(*) FROM dbo.VotingCodes WHERE ElectionId = '{id}'"));
        Assert.Equal(5, await CountAsync($"SELECT CodesIssued FROM dbo.Elections WHERE Id = '{id}'"));
        Assert.Null(await Admin().IssueSlipsAsync(id, 5));
        Assert.Equal(5, await CountAsync($"SELECT COUNT(*) FROM dbo.VotingCodes WHERE ElectionId = '{id}'"));
    }

    [Fact]
    public async Task Made_slips_let_the_vote_open_and_each_code_votes_once()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 0, open: false);
        // The seed marks no slips issued for a zero count, so the draft is ready for slips.
        IReadOnlyList<string> codes = (await Admin().IssueSlipsAsync(seed.ElectionId, 2))!;

        Assert.True(await Admin().OpenAsync(seed.ElectionId));
        BallotSelection[] pick = [new BallotSelection(seed.ContestId, seed.ChoiceIds[0])];
        Assert.Equal(CastStatus.Cast, (await database.Caster.CastAsync(seed.ElectionId, codes[0], pick, [])).Status);
        Assert.Equal(CastStatus.CodeRefused, (await database.Caster.CastAsync(seed.ElectionId, codes[0], pick, [])).Status);
        Assert.Equal(CastStatus.Cast, (await database.Caster.CastAsync(seed.ElectionId, codes[1], pick, [])).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(SlipLimits.Max + 1)]
    public async Task A_slip_count_out_of_range_is_refused(int count)
    {
        CreateElectionResponse created = await Admin().CreateAsync(Request(NewSlug()));

        Assert.Null(await Admin().IssueSlipsAsync(created.Id!.Value, count));
        Assert.Equal(0, await CountAsync($"SELECT CodesIssued FROM dbo.Elections WHERE Id = '{created.Id}'"));
    }

    [Fact]
    public async Task Slips_cannot_be_made_for_an_open_election()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 2);

        Assert.Null(await Admin().IssueSlipsAsync(seed.ElectionId, 3));
    }

    private async Task<int> CountAsync(string sql)
    {
        await using SqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
