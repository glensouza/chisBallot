using ChisBallot.Data;
using Microsoft.Data.SqlClient;

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
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.Ballots WHERE ElectionId = @e", seed.ElectionId));
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

        await using SqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync();

        List<string> ballotColumns = await ColumnsAsync(connection, "Ballots");
        Assert.Equal(["ElectionId", "Id", "ReceiptCode"], ballotColumns.Order().ToList());
        Assert.Equal(["BallotId", "ChoiceId", "ContestId"], (await ColumnsAsync(connection, "BallotSelections")).Order().ToList());
        Assert.Equal(["BallotId", "ContestId", "Name", "NameKey"], (await ColumnsAsync(connection, "BallotWriteIns")).Order().ToList());

        // No time, sequence, identity or row-version columns on any ballot table.
        string orderingColumns = """
            SELECT COUNT(*)
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id IN (OBJECT_ID('dbo.Ballots'), OBJECT_ID('dbo.BallotSelections'), OBJECT_ID('dbo.BallotWriteIns'))
              AND (c.is_identity = 1
                   OR c.is_computed = 1
                   OR t.name IN ('datetime', 'datetime2', 'datetimeoffset', 'smalldatetime', 'date', 'time', 'timestamp', 'rowversion'))
            """;
        await using SqlCommand ordering = new(orderingColumns, connection);
        Assert.Equal(0, (int)(await ordering.ExecuteScalarAsync())!);

        // The ballot's id is not sequential, and nothing references the code table.
        await using SqlCommand links = new("""
            SELECT COUNT(*)
            FROM sys.foreign_keys
            WHERE referenced_object_id = OBJECT_ID('dbo.VotingCodes') AND parent_object_id <> OBJECT_ID('dbo.VotingCodes')
            """, connection);
        Assert.Equal(0, (int)(await links.ExecuteScalarAsync())!);

        await using SqlCommand sequential = new("""
            SELECT COUNT(*)
            FROM sys.default_constraints
            WHERE parent_object_id = OBJECT_ID('dbo.Ballots') AND definition LIKE '%sequentialid%'
            """, connection);
        Assert.Equal(0, (int)(await sequential.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task The_voting_site_cannot_read_or_write_ballots_codes_or_tallies_directly()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);

        foreach (string sql in new[]
        {
            "SELECT COUNT(*) FROM dbo.Ballots",
            "SELECT COUNT(*) FROM dbo.BallotSelections",
            "SELECT COUNT(*) FROM dbo.BallotWriteIns",
            "SELECT COUNT(*) FROM dbo.VotingCodes",
            "UPDATE dbo.VotingCodes SET Used = 0",
            "DELETE FROM dbo.Ballots",
            "UPDATE dbo.Elections SET Status = 2",
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
            "SELECT COUNT(*) FROM dbo.Ballots",
            "SELECT COUNT(*) FROM dbo.BallotSelections",
            "SELECT COUNT(*) FROM dbo.BallotWriteIns",
            "SELECT COUNT(*) FROM dbo.VotingCodes",
            "DELETE FROM dbo.VotingCodes",
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

        SqlException whileOpen = await Assert.ThrowsAsync<SqlException>(() => ReadResultsAsync(database.VoteConnectionString, seed.ElectionId));
        Assert.Equal(50001, whileOpen.Number);
        await Assert.ThrowsAsync<SqlException>(() => ReadResultsAsync(database.AdminConnectionString, seed.ElectionId));

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

        await using SqlConnection connection = new(database.VoteConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new("dbo.GetTurnout", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@ElectionId", seed.ElectionId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(2, reader.GetInt32(2));
    }

    [Fact]
    public async Task A_closed_election_is_final_and_settings_freeze_once_open()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database);

        await using SqlConnection connection = new(database.AdminConnectionString);
        await connection.OpenAsync();

        await AssertFailsAsync(connection, "UPDATE dbo.Elections SET ShowLiveResults = 1 WHERE Id = @e", seed.ElectionId);
        await AssertFailsAsync(connection, "UPDATE dbo.Elections SET IsDemo = 1 WHERE Id = @e", seed.ElectionId);
        await AssertFailsAsync(connection, "UPDATE dbo.Elections SET Status = 0 WHERE Id = @e", seed.ElectionId);

        await CloseAsync(seed.ElectionId);

        await AssertFailsAsync(connection, "UPDATE dbo.Elections SET Status = 1 WHERE Id = @e", seed.ElectionId);
        await AssertFailsAsync(connection, "UPDATE dbo.Elections SET Name = N'Changed' WHERE Id = @e", seed.ElectionId);
    }

    [Fact]
    public async Task Closing_deletes_only_the_unused_codes()
    {
        ElectionSeed seed = await ElectionSeed.CreateAsync(database, codeCount: 3);
        await database.Caster.CastAsync(seed.ElectionId, seed.Codes[0], Pick(seed, 0), []);

        await CloseAsync(seed.ElectionId);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.VotingCodes WHERE ElectionId = @e", seed.ElectionId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.VotingCodes WHERE ElectionId = @e AND Used = 1", seed.ElectionId));
    }

    private async Task CloseAsync(Guid electionId)
    {
        await using SqlConnection connection = new(database.AdminConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new("dbo.CloseElection", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@ElectionId", electionId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql, Guid electionId)
    {
        await using SqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@e", electionId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ColumnsAsync(SqlConnection connection, string table)
    {
        List<string> columns = [];
        await using SqlCommand command = new("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t", connection);
        command.Parameters.AddWithValue("@t", table);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task AssertDeniedAsync(string connectionString, string sql)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        SqlException exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains(exception.Number, new[] { 229, 230, 262 });
    }

    private static async Task AssertFailsAsync(SqlConnection connection, string sql, Guid electionId)
    {
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@e", electionId);
        SqlException exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(50002, exception.Number);
    }

    private static async Task<List<(Guid? Choice, string? WriteIn, int Votes)>> ReadResultsAsync(string connectionString, Guid electionId)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new("dbo.GetResults", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@ElectionId", electionId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        List<(Guid? Choice, string? WriteIn, int Votes)> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add((reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3)));
        }

        return rows;
    }
}
