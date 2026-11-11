using ChisBallot.Data;
using Microsoft.Data.SqlClient;

namespace ChisBallot.Tests;

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

        await using SqlConnection connection = new(database.AdminConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "INSERT dbo.Elections (Id, Slug, Name, Status, CodesIssued, ShowLiveResults) VALUES (@e, @slug, N'Test', 0, @issued, @live)",
            ("@e", electionId), ("@slug", electionId.ToString("N")), ("@issued", codeCount), ("@live", showLiveResults));
        await ExecuteAsync(connection, "INSERT dbo.Contests (Id, ElectionId, Title, MaxSelections, AllowWriteIn, SortOrder) VALUES (@c, @e, N'Question', @max, @w, 1)",
            ("@c", contestId), ("@e", electionId), ("@max", maxSelections), ("@w", allowWriteIn));

        for (int index = 0; index < choiceIds.Length; index++)
        {
            await ExecuteAsync(connection, "INSERT dbo.Choices (Id, ContestId, Label, SortOrder) VALUES (@id, @c, @label, @n)",
                ("@id", choiceIds[index]), ("@c", contestId), ("@label", "Choice " + index), ("@n", index));
        }

        foreach (string code in codes)
        {
            await ExecuteAsync(connection, "INSERT dbo.VotingCodes (ElectionId, CodeHash) VALUES (@e, @h)",
                ("@e", electionId), ("@h", VotingCode.Hash(code)));
        }

        if (open)
        {
            await ExecuteAsync(connection, "UPDATE dbo.Elections SET Status = 1 WHERE Id = @e", ("@e", electionId));
        }

        return new ElectionSeed(electionId, contestId, choiceIds, codes);
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
