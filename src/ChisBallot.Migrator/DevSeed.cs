using ChisBallot.Data;
using Npgsql;
using Serilog;

namespace ChisBallot.Migrator;

/// <summary>
/// A practice election for local development: a pick-three question with a
/// write-in, and a yes or no question, with live results and random order on
/// so every option can be seen. The slips are printed to the console because
/// there is no printer; this is the only place a code is ever written out.
/// </summary>
public static class DevSeed
{
    private const string Slug = "practice";
    private const int SlipCount = 8;

    public static async Task RunAsync(string connectionString, string siteUrl, ILogger logger)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand exists = new("SELECT COUNT(*) FROM ballot.elections WHERE slug = @slug", connection))
        {
            exists.Parameters.AddWithValue("slug", Slug);
            if ((long)(await exists.ExecuteScalarAsync())! != 0)
            {
                logger.Information("The practice election already exists, so it was left alone.");
                return;
            }
        }

        Guid electionId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results, shuffle_contests, shuffle_choices, is_demo) VALUES (@e, @slug, 'Practice vote', 0, @issued, true, true, true, true)",
            ("e", electionId), ("slug", Slug), ("issued", SlipCount));

        Guid pickThree = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order) VALUES (@c, @e, 'Pick three field trip spots', 3, 3, true, 1)",
            ("c", pickThree), ("e", electionId));
        string[] spots = ["Science museum", "Zoo", "Aquarium", "State capitol", "Botanical garden", "Planetarium"];
        for (int index = 0; index < spots.Length; index++)
        {
            await ExecuteAsync(connection, "INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), @c, @label, @n)",
                ("c", pickThree), ("label", spots[index]), ("n", index));
        }

        Guid lunch = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order) VALUES (@c, @e, 'Should lunch be longer?', 1, 1, false, 2)",
            ("c", lunch), ("e", electionId));
        string[] answers = ["Yes", "No"];
        for (int index = 0; index < answers.Length; index++)
        {
            await ExecuteAsync(connection, "INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), @c, @label, @n)",
                ("c", lunch), ("label", answers[index]), ("n", index));
        }

        List<string> codes = [];
        for (int index = 0; index < SlipCount; index++)
        {
            string code = VotingCode.Generate();
            codes.Add(code);
            await ExecuteAsync(connection, "INSERT INTO ballot.voting_codes (election_id, code_hash) VALUES (@e, @h)",
                ("e", electionId), ("h", VotingCode.Hash(code)));
        }

        await ExecuteAsync(connection, "UPDATE ballot.elections SET status = 1 WHERE id = @e", ("e", electionId));

        logger.Information("Practice election is open. Election page: {Url}/e/{Slug}", siteUrl, Slug);
        foreach (string code in codes)
        {
            logger.Information("Practice slip: {Url}/v/{Slug}#{Code}", siteUrl, Slug, code);
        }
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
