using ChisBallot.Data;
using Npgsql;

namespace ChisBallot.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CastBallotTests(DatabaseFixture database)
{
    private static BallotSelection[] Pick(ElectionSeed seed, int choice)
    {
        return [new BallotSelection(seed.ContestId, seed.ChoiceIds[choice])];
    }

    [Fact]
    public async Task A_code_works_once()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);

        CastOutcome first = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);
        CastOutcome second = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 1), []);

        Assert.Equal(CastStatus.Cast, first.Status);
        Assert.NotNull(first.ReceiptCode);
        Assert.Equal(CastStatus.CodeRefused, second.Status);
        Assert.Null(second.ReceiptCode);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM ballot.ballots WHERE election_id = @e", seed.ElectionId));
    }

    [Fact]
    public async Task A_code_from_another_election_is_refused()
    {
        ElectionSeed first = await ElectionSeed.CreateAsync(database);
        ElectionSeed second = await ElectionSeed.CreateAsync(database);

        CastOutcome outcome = await database.Caster.CastAsync(second.ElectionId, first.Codes[0], Pick(second, 0), []);
        CastOutcome stillGood = await database.Caster.CastAsync(first.ElectionId, first.Codes[0], Pick(first, 0), []);

        Assert.Equal(CastStatus.CodeRefused, outcome.Status);
        Assert.Equal(CastStatus.Cast, stillGood.Status);
    }

    [Fact]
    public async Task A_code_is_refused_unless_the_election_is_open()
    {
        ElectionSeed draft = await ElectionSeed.CreateAsync(database, open: false);
        ElectionSeed closed = await ElectionSeed.CreateAsync(database);
        await CloseAsync(closed.ElectionId);

        CastOutcome notOpen = await database.Caster.CastAsync(draft.ElectionId, draft.Codes[0], Pick(draft, 0), []);
        CastOutcome afterClose = await database.Caster.CastAsync(closed.ElectionId, closed.Codes[0], Pick(closed, 0), []);

        Assert.Equal(CastStatus.CodeRefused, notOpen.Status);
        Assert.Equal(CastStatus.CodeRefused, afterClose.Status);
    }

    [Fact]
    public async Task Choices_that_do_not_fit_are_refused_and_the_code_stays_usable()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);
        ElectionSeed other = await ElectionSeed.CreateAsync(database);

        CastOutcome foreignChoice = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(other, 0), []);
        CastOutcome tooMany = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], [.. Pick(seed, 0), .. Pick(seed, 1)], []);
        CastOutcome writeInNotAllowed = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], [], [new WriteInEntry(seed.ContestId, "Pat Lee")]);
        CastOutcome valid = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);

        Assert.Equal(CastStatus.SelectionsRefused, foreignChoice.Status);
        Assert.Equal(CastStatus.SelectionsRefused, tooMany.Status);
        Assert.Equal(CastStatus.SelectionsRefused, writeInNotAllowed.Status);
        Assert.Equal(CastStatus.Cast, valid.Status);
    }

    [Fact]
    public async Task A_write_in_counts_against_the_limit_and_is_normalized()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, allowWriteIn: true, maxSelections: 2, showLiveResults: true);

        CastOutcome overLimit = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], [.. Pick(seed, 0), .. Pick(seed, 1)], [new WriteInEntry(seed.ContestId, "Pat Lee")]);
        CastOutcome first = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), [new WriteInEntry(seed.ContestId, "  Pat   LEE ")]);
        CastOutcome second = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[1], [], [new WriteInEntry(seed.ContestId, "pat lee")]);
        CastOutcome invalid = await database.Caster.CastAsync(seed.ElectionId, seed.Codes[2], [], [new WriteInEntry(seed.ContestId, "<script>")]);
        await CloseAsync(seed.ElectionId);

        Assert.Equal(CastStatus.SelectionsRefused, overLimit.Status);
        Assert.Equal(CastStatus.Cast, first.Status);
        Assert.Equal(CastStatus.Cast, second.Status);
        Assert.Equal(CastStatus.SelectionsRefused, invalid.Status);

        List<(Guid? Choice, string? WriteIn, int Votes)> results = await ReadResultsAsync(database.VoteConnectionString, seed.ElectionId);
        Assert.Contains(results, row => row.WriteIn == "pat lee" && row.Votes == 2);
    }

    [Fact]
    public async Task A_ballot_row_has_nothing_that_links_it_to_a_code_or_to_voting_order()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);

        await using NpgsqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync();

        List<string> ballotColumns = await ColumnsAsync(connection, "ballots");
        Assert.Equal(["election_id", "id", "receipt_code"], ballotColumns.Order().ToList());
        Assert.Equal(["ballot_id", "choice_id", "contest_id"], (await ColumnsAsync(connection, "ballot_selections")).Order().ToList());
        Assert.Equal(["ballot_id", "contest_id", "name", "name_key"], (await ColumnsAsync(connection, "ballot_write_ins")).Order().ToList());

        // No time, sequence, identity or generated columns on any ballot table.
        await using NpgsqlCommand ordering = new("""
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'ballot'
              AND table_name IN ('ballots', 'ballot_selections', 'ballot_write_ins')
              AND (is_identity = 'YES'
                   OR is_generated <> 'NEVER'
                   OR column_default LIKE 'nextval%'
                   OR data_type LIKE 'timestamp%'
                   OR data_type IN ('date', 'time without time zone', 'time with time zone'))
            """, connection);
        Assert.Equal(0L, (long)(await ordering.ExecuteScalarAsync())!);

        // Nothing but the voting_codes table itself touches the code table.
        await using NpgsqlCommand links = new("""
            SELECT COUNT(*)
            FROM pg_constraint
            WHERE contype = 'f'
              AND confrelid = 'ballot.voting_codes'::regclass
              AND conrelid <> confrelid
            """, connection);
        Assert.Equal(0L, (long)(await links.ExecuteScalarAsync())!);

        // The ballot's id has no default at all, so it cannot come from a sequence.
        await using NpgsqlCommand defaults = new("""
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'ballot' AND table_name = 'ballots' AND column_name = 'id' AND column_default IS NOT NULL
            """, connection);
        Assert.Equal(0L, (long)(await defaults.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task The_voting_site_cannot_read_or_write_ballots_codes_or_tallies_directly()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);

        foreach (string sql in new[]
        {
            "SELECT COUNT(*) FROM ballot.ballots",
            "SELECT COUNT(*) FROM ballot.ballot_selections",
            "SELECT COUNT(*) FROM ballot.ballot_write_ins",
            "SELECT COUNT(*) FROM ballot.voting_codes",
            "UPDATE ballot.voting_codes SET used = false",
            "DELETE FROM ballot.ballots",
            "UPDATE ballot.elections SET status = 2",
        })
        {
            await AssertDeniedAsync(database.VoteConnectionString, sql);
        }
    }

    [Fact]
    public async Task The_admin_site_cannot_read_ballots_or_codes()
    {
        foreach (string sql in new[]
        {
            "SELECT COUNT(*) FROM ballot.ballots",
            "SELECT COUNT(*) FROM ballot.ballot_selections",
            "SELECT COUNT(*) FROM ballot.ballot_write_ins",
            "SELECT COUNT(*) FROM ballot.voting_codes",
            "DELETE FROM ballot.voting_codes",
        })
        {
            await AssertDeniedAsync(database.AdminConnectionString, sql);
        }
    }

    [Fact]
    public async Task Results_are_unreadable_until_the_election_is_closed()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 1), []);

        PostgresException whileOpen = await Assert.ThrowsAsync<PostgresException>(() => ReadResultsAsync(database.VoteConnectionString, seed.ElectionId));
        Assert.Equal("CB001", whileOpen.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => ReadResultsAsync(database.AdminConnectionString, seed.ElectionId));

        await CloseAsync(seed.ElectionId);

        List<(Guid? Choice, string? WriteIn, int Votes)> results = await ReadResultsAsync(database.VoteConnectionString, seed.ElectionId);
        Assert.Equal(1, results.Single(row => row.Choice == seed.ChoiceIds[1]).Votes);
        Assert.Equal(0, results.Single(row => row.Choice == seed.ChoiceIds[0]).Votes);
    }

    [Fact]
    public async Task Live_results_show_counts_while_open_only_when_the_election_turned_them_on()
    {
        ElectionSeed live = await ElectionSeed.CreateAsync(database, showLiveResults: true, allowWriteIn: true);
        await database.Caster.CastAsync(live.ElectionId, live.Codes[0], Pick(live, 2), []);
        await database.Caster.CastAsync(live.ElectionId, live.Codes[1], [], [new WriteInEntry(live.ContestId, "Pat Lee")]);

        List<(Guid? Choice, string? WriteIn, int Votes)> results = await ReadResultsAsync(database.VoteConnectionString, live.ElectionId);

        Assert.Equal(1, results.Single(row => row.Choice == live.ChoiceIds[2]).Votes);
        (Guid? Choice, string? WriteIn, int Votes) writeIns = results.Single(row => row.Choice is null);
        Assert.Null(writeIns.WriteIn);
        Assert.Equal(1, writeIns.Votes);
    }

    [Fact]
    public async Task Turnout_is_public_and_ballots_never_exceed_codes_used()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 3);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[1], Pick(seed, 1), []);

        await using NpgsqlConnection connection = new(database.VoteConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT codes_issued, codes_used, ballots_counted FROM ballot.get_turnout(@e)", connection);
        command.Parameters.AddWithValue("@e", seed.ElectionId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(2, reader.GetInt32(2));
    }

    [Fact]
    public async Task A_closed_election_is_final_and_settings_freeze_once_open()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);

        await using NpgsqlConnection connection = new(database.AdminConnectionString);
        await connection.OpenAsync();

        await AssertFailsAsync(connection, "UPDATE ballot.elections SET show_live_results = true WHERE id = @e", seed.ElectionId);
        await AssertFailsAsync(connection, "UPDATE ballot.elections SET is_demo = true WHERE id = @e", seed.ElectionId);
        await AssertFailsAsync(connection, "UPDATE ballot.elections SET status = 0 WHERE id = @e", seed.ElectionId);

        await CloseAsync(seed.ElectionId);

        await AssertFailsAsync(connection, "UPDATE ballot.elections SET status = 1 WHERE id = @e", seed.ElectionId);
        await AssertFailsAsync(connection, "UPDATE ballot.elections SET name = 'Changed' WHERE id = @e", seed.ElectionId);
    }

    [Fact]
    public async Task Closing_deletes_only_the_unused_codes()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 3);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);

        await CloseAsync(seed.ElectionId);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM ballot.voting_codes WHERE election_id = @e", seed.ElectionId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM ballot.voting_codes WHERE election_id = @e AND used", seed.ElectionId));
    }

    private async Task CloseAsync(Guid electionId)
    {
        await using NpgsqlConnection connection = new(database.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT ballot.close_election(@e)", connection);
        command.Parameters.AddWithValue("@e", electionId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql, Guid electionId)
    {
        await using NpgsqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@e", electionId);
        return (int)(long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ColumnsAsync(NpgsqlConnection connection, string table)
    {
        List<string> columns = [];
        await using NpgsqlCommand command = new("SELECT column_name FROM information_schema.columns WHERE table_schema = 'ballot' AND table_name = @t", connection);
        command.Parameters.AddWithValue("@t", table);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task AssertDeniedAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", exception.SqlState);
    }

    private static async Task AssertFailsAsync(NpgsqlConnection connection, string sql, Guid electionId)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@e", electionId);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("CB002", exception.SqlState);
    }

    private static async Task<List<(Guid? Choice, string? WriteIn, int Votes)>> ReadResultsAsync(string connectionString, Guid electionId)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT contest_id, choice_id, write_in, votes FROM ballot.get_results(@e)", connection);
        command.Parameters.AddWithValue("@e", electionId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<(Guid? Choice, string? WriteIn, int Votes)> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add((reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3)));
        }

        return rows;
    }
}
