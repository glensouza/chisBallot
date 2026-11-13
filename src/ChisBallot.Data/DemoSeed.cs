using Npgsql;

namespace ChisBallot.Data;

/// <summary>
/// Makes the two demo elections: <c>demo</c>, open with nothing in it so a room
/// can vote, and <c>demo-tie</c>, already closed with made-up ballots that end
/// in a tie. Run with the owner connection, because only the owner can delete.
/// <see cref="EnsureAsync"/> does nothing if they exist; <see cref="ResetAsync"/>
/// deletes both (demos only, never a real election) and makes them again, so a
/// demo can be run as many times as needed.
/// </summary>
public static class DemoSeed
{
    public const string OpenSlug = "demo";
    public const string TieSlug = "demo-tie";

    // The two demos and any revote made from them (a revote of a demo is a demo too).
    private const string DemoIds = """
        (WITH RECURSIVE d AS (
            SELECT id FROM ballot.elections WHERE is_demo AND slug IN ('demo', 'demo-tie')
            UNION
            SELECT e.id FROM ballot.elections e JOIN d ON e.revote_of = d.id WHERE e.is_demo)
         SELECT id FROM d)
        """;

    public static async Task EnsureAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await CreateMissingAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task ResetAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        string demoBallots = $"(SELECT id FROM ballot.ballots WHERE election_id IN {DemoIds})";
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.ballot_selections WHERE ballot_id IN {demoBallots}", cancellationToken);
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.ballot_write_ins WHERE ballot_id IN {demoBallots}", cancellationToken);
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.ballots WHERE election_id IN {DemoIds}", cancellationToken);
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.voting_codes WHERE election_id IN {DemoIds}", cancellationToken);
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.choices WHERE contest_id IN (SELECT id FROM ballot.contests WHERE election_id IN {DemoIds})", cancellationToken);
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.contests WHERE election_id IN {DemoIds}", cancellationToken);
        await ExecuteAsync(connection, transaction, $"DELETE FROM ballot.elections WHERE id IN {DemoIds}", cancellationToken);

        await CreateMissingAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task CreateMissingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(connection, transaction, OpenSlug, cancellationToken))
        {
            await CreateOpenDemoAsync(connection, transaction, cancellationToken);
        }

        if (!await ExistsAsync(connection, transaction, TieSlug, cancellationToken))
        {
            await CreateTieDemoAsync(connection, transaction, cancellationToken);
        }
    }

    private static async Task CreateOpenDemoAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        Guid election = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, "INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results, shuffle_contests, shuffle_choices, is_demo) VALUES (@e, @slug, 'Demo vote', 0, 0, true, true, true, true)", cancellationToken, ("e", election), ("slug", OpenSlug));

        await AddContestAsync(connection, transaction, election, "Pick three field trip spots", 3, 3, true, 1, ["Science museum", "Zoo", "Aquarium", "State capitol", "Botanical garden", "Planetarium"], cancellationToken);
        await AddContestAsync(connection, transaction, election, "Should lunch be longer?", 1, 1, false, 2, ["Yes", "No"], cancellationToken);

        // Opening needs no slips: the demo page hands them out one at a time.
        await ExecuteAsync(connection, transaction, "UPDATE ballot.elections SET status = 1 WHERE id = @e", cancellationToken, ("e", election));
    }

    private static async Task CreateTieDemoAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Two seats. Ada wins one clearly; Ben and Cy tie for the other.
        string[] names = ["Ada", "Ben", "Cy", "Dee"];
        int[] votes = [6, 3, 3, 1];

        Guid election = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, "INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results, shuffle_contests, shuffle_choices, is_demo) VALUES (@e, @slug, 'Demo vote: a tie', 0, @n, false, false, false, true)", cancellationToken, ("e", election), ("slug", TieSlug), ("n", votes.Sum()));

        Guid contest = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, "INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order) VALUES (@c, @e, 'Two student representatives', 2, 1, false, 1)", cancellationToken, ("c", contest), ("e", election));

        for (int person = 0; person < names.Length; person++)
        {
            Guid choice = Guid.NewGuid();
            await ExecuteAsync(connection, transaction, "INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (@ch, @c, @label, @n)", cancellationToken, ("ch", choice), ("c", contest), ("label", names[person]), ("n", person));

            for (int vote = 0; vote < votes[person]; vote++)
            {
                Guid ballot = Guid.NewGuid();
                await ExecuteAsync(connection, transaction, "INSERT INTO ballot.ballots (id, election_id, receipt_code) VALUES (@b, @e, @r)", cancellationToken, ("b", ballot), ("e", election), ("r", ReceiptCode.Generate()));
                await ExecuteAsync(connection, transaction, "INSERT INTO ballot.ballot_selections (ballot_id, contest_id, choice_id) VALUES (@b, @c, @ch)", cancellationToken, ("b", ballot), ("c", contest), ("ch", choice));
                await ExecuteAsync(connection, transaction, "INSERT INTO ballot.voting_codes (election_id, code_hash, used) VALUES (@e, @h, true)", cancellationToken, ("e", election), ("h", VotingCode.Hash(VotingCode.Generate())));
            }
        }

        await ExecuteAsync(connection, transaction, "UPDATE ballot.elections SET status = 1 WHERE id = @e", cancellationToken, ("e", election));
        await ExecuteAsync(connection, transaction, "SELECT ballot.close_election(@e)", cancellationToken, ("e", election));
    }

    private static async Task AddContestAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid election, string title, int seats, int maxSelections, bool writeIn, int order, string[] labels, CancellationToken cancellationToken)
    {
        Guid contest = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, "INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order) VALUES (@c, @e, @title, @seats, @max, @wi, @o)", cancellationToken,
            ("c", contest), ("e", election), ("title", title), ("seats", seats), ("max", maxSelections), ("wi", writeIn), ("o", order));
        for (int index = 0; index < labels.Length; index++)
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), @c, @label, @n)", cancellationToken, ("c", contest), ("label", labels[index]), ("n", index));
        }
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string slug, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new("SELECT count(*) FROM ballot.elections WHERE slug = @slug", connection, transaction);
        command.Parameters.AddWithValue("slug", slug);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))! != 0;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection, transaction);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
