using ChisBallot.Data;
using Microsoft.Data.SqlClient;
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
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using (SqlCommand exists = new("SELECT COUNT(*) FROM dbo.Elections WHERE Slug = @slug", connection))
        {
            exists.Parameters.AddWithValue("@slug", Slug);
            if ((int)(await exists.ExecuteScalarAsync())! != 0)
            {
                logger.Information("The practice election already exists, so it was left alone.");
                return;
            }
        }

        Guid electionId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT dbo.Elections (Id, Slug, Name, Status, CodesIssued, ShowLiveResults, ShuffleContests, ShuffleChoices, IsDemo) VALUES (@e, @slug, N'Practice vote', 0, @issued, 1, 1, 1, 1)",
            ("@e", electionId), ("@slug", Slug), ("@issued", SlipCount));

        Guid pickThree = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT dbo.Contests (Id, ElectionId, Title, Seats, MaxSelections, AllowWriteIn, SortOrder) VALUES (@c, @e, N'Pick three field trip spots', 3, 3, 1, 1)",
            ("@c", pickThree), ("@e", electionId));
        string[] spots = ["Science museum", "Zoo", "Aquarium", "State capitol", "Botanical garden", "Planetarium"];
        for (int index = 0; index < spots.Length; index++)
        {
            await ExecuteAsync(connection, "INSERT dbo.Choices (Id, ContestId, Label, SortOrder) VALUES (NEWID(), @c, @label, @n)",
                ("@c", pickThree), ("@label", spots[index]), ("@n", index));
        }

        Guid lunch = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT dbo.Contests (Id, ElectionId, Title, Seats, MaxSelections, AllowWriteIn, SortOrder) VALUES (@c, @e, N'Should lunch be longer?', 1, 1, 0, 2)",
            ("@c", lunch), ("@e", electionId));
        string[] answers = ["Yes", "No"];
        for (int index = 0; index < answers.Length; index++)
        {
            await ExecuteAsync(connection, "INSERT dbo.Choices (Id, ContestId, Label, SortOrder) VALUES (NEWID(), @c, @label, @n)",
                ("@c", lunch), ("@label", answers[index]), ("@n", index));
        }

        List<string> codes = [];
        for (int index = 0; index < SlipCount; index++)
        {
            string code = VotingCode.Generate();
            codes.Add(code);
            await ExecuteAsync(connection, "INSERT dbo.VotingCodes (ElectionId, CodeHash) VALUES (@e, @h)",
                ("@e", electionId), ("@h", VotingCode.Hash(code)));
        }

        await ExecuteAsync(connection, "UPDATE dbo.Elections SET Status = 1 WHERE Id = @e", ("@e", electionId));

        logger.Information("Practice election is open. Election page: {Url}/e/{Slug}", siteUrl, Slug);
        foreach (string code in codes)
        {
            logger.Information("Practice slip: {Url}/v/{Slug}#{Code}", siteUrl, Slug, code);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using SqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
