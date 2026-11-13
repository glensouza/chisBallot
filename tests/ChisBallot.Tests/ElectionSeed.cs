namespace ChisBallot.Tests;

using ChisBallot.Data;
using Npgsql;

/// <summary>
/// Builds an election the way the admin site would: as the admin database
/// user, with a Draft election that is then opened.
/// </summary>
public sealed class ElectionSeed
{
    private ElectionSeed(Guid electionId, Guid contestId, Guid[] choiceIds, string[] codes)
    {
        this.ElectionId = electionId;
        this.ContestId = contestId;
        this.ChoiceIds = choiceIds;
        this.Codes = codes;
    }

    public Guid ElectionId { get; }

    public Guid ContestId { get; }

    public Guid[] ChoiceIds { get; }

    public string[] Codes { get; }

    public static async Task<ElectionSeed> CreateAsync(
        DatabaseFixture database,
        int codeCount = 3,
        bool open = true,
        bool showLiveResults = false,
        bool allowWriteIn = false,
        int maxSelections = 1)
    {
        Guid electionId = Guid.NewGuid();
        Guid contestId = Guid.NewGuid();
        Guid[] choiceIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        string[] codes = Enumerable.Range(0, codeCount).Select(_ => VotingCode.Generate()).ToArray();

        await using NpgsqlConnection connection = new(database.AdminConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results) VALUES (@e, @slug, 'Test', 0, @issued, @live)",
            ("@e", electionId), ("@slug", electionId.ToString("N")), ("@issued", codeCount), ("@live", showLiveResults));
        await ExecuteAsync(connection, "INSERT INTO ballot.contests (id, election_id, title, max_selections, allow_write_in, sort_order) VALUES (@c, @e, 'Question', @max, @w, 1)",
            ("@c", contestId), ("@e", electionId), ("@max", maxSelections), ("@w", allowWriteIn));

        for (int index = 0; index < choiceIds.Length; index++)
        {
            await ExecuteAsync(connection, "INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (@id, @c, @label, @n)",
                ("@id", choiceIds[index]), ("@c", contestId), ("@label", "Choice " + index), ("@n", index));
        }

        foreach (string code in codes)
        {
            await ExecuteAsync(connection, "INSERT INTO ballot.voting_codes (election_id, code_hash) VALUES (@e, @h)",
                ("@e", electionId), ("@h", VotingCode.Hash(code)));
        }

        if (open)
        {
            await ExecuteAsync(connection, "UPDATE ballot.elections SET status = 1 WHERE id = @e", ("@e", electionId));
        }

        return new ElectionSeed(electionId, contestId, choiceIds, codes);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
