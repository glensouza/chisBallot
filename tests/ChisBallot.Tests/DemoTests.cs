namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;
using Npgsql;

[Collection(DatabaseCollection.Name)]
public sealed class DemoTests(DatabaseFixture database)
{
    [Fact]
    public async Task Seeding_makes_an_open_demo_and_a_closed_tie_and_is_safe_to_repeat()
    {
        await DemoSeed.EnsureAsync(database.OwnerConnectionString);
        await DemoSeed.EnsureAsync(database.OwnerConnectionString);

        Assert.Equal(1, await this.CountAsync("SELECT count(*) FROM ballot.elections WHERE slug = 'demo' AND is_demo AND status = 1"));
        Assert.Equal(1, await this.CountAsync("SELECT count(*) FROM ballot.elections WHERE slug = 'demo-tie' AND is_demo AND status = 2"));
        Assert.Equal(13, await this.CountAsync("SELECT count(*) FROM ballot.ballots b JOIN ballot.elections e ON e.id = b.election_id WHERE e.slug = 'demo-tie'"));
    }

    [Fact]
    public async Task A_demo_slip_can_be_issued_and_used_to_vote()
    {
        await DemoSeed.EnsureAsync(database.OwnerConnectionString);
        ElectionReader reader = new(database.VoteConnectionString);
        ElectionView election = (await reader.FindBySlugAsync("demo", includeDrafts: false))!;

        string? code = await new DemoSlips(database.VoteConnectionString).IssueAsync(election.Id);
        Assert.NotNull(code);

        Guid contest = election.Contests[1].Id;
        CastOutcome outcome = await database.Caster.CastAsync(election.Id, code, [new BallotSelection(contest, election.Contests[1].Choices[0].Id)], []);
        Assert.Equal(CastStatus.Cast, outcome.Status);
    }

    [Fact]
    public async Task A_slip_cannot_be_issued_for_a_real_or_closed_election()
    {
        DemoSlips slips = new(database.VoteConnectionString);

        ElectionSeed real = await ElectionSeed.CreateAsync(database);
        Assert.Null(await slips.IssueAsync(real.ElectionId));

        await DemoSeed.EnsureAsync(database.OwnerConnectionString);
        ElectionView tie = (await new ElectionReader(database.VoteConnectionString).FindBySlugAsync("demo-tie", includeDrafts: false))!;
        Assert.Null(await slips.IssueAsync(tie.Id));
    }

    [Fact]
    public async Task Reset_clears_demo_ballots_and_leaves_real_elections_alone()
    {
        ElectionSeed real = await ElectionSeed.CreateAsync(database);
        await DemoSeed.EnsureAsync(database.OwnerConnectionString);
        ElectionView election = (await new ElectionReader(database.VoteConnectionString).FindBySlugAsync("demo", includeDrafts: false))!;
        string code = (await new DemoSlips(database.VoteConnectionString).IssueAsync(election.Id))!;
        await database.Caster.CastAsync(election.Id, code, [new BallotSelection(election.Contests[1].Id, election.Contests[1].Choices[0].Id)], []);

        await DemoSeed.ResetAsync(database.OwnerConnectionString);

        ElectionView fresh = (await new ElectionReader(database.VoteConnectionString).FindBySlugAsync("demo", includeDrafts: false))!;
        Assert.NotEqual(election.Id, fresh.Id);
        Assert.Equal(0, await this.CountAsync($"SELECT count(*) FROM ballot.ballots WHERE election_id = '{fresh.Id}'"));
        Assert.Equal(1, await this.CountAsync($"SELECT count(*) FROM ballot.elections WHERE id = '{real.ElectionId}'"));
        Assert.Equal(1, await this.CountAsync("SELECT count(*) FROM ballot.elections WHERE slug = 'demo-tie'"));
    }

    private async Task<long> CountAsync(string sql)
    {
        await using NpgsqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
