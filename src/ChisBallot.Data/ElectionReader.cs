using ChisBallot.Contracts;
using Microsoft.Data.SqlClient;

namespace ChisBallot.Data;

/// <summary>
/// Reads the parts of an election that are not secret: what the ballot page
/// shows, the admin list, and turnout. Never touches ballots; the database
/// users have no right to.
/// </summary>
public sealed class ElectionReader(string connectionString)
{
    public async Task<ElectionView?> FindBySlugAsync(string slug, bool includeDrafts, CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        Guid id;
        string name;
        int status;
        bool showLiveResults;
        bool shuffleContests;
        bool shuffleChoices;
        bool isDemo;

        await using (SqlCommand command = new("""
            SELECT Id, Name, Status, ShowLiveResults, ShuffleContests, ShuffleChoices, IsDemo
            FROM dbo.Elections
            WHERE Slug = @slug AND (@drafts = 1 OR Status <> 0)
            """, connection))
        {
            command.Parameters.AddWithValue("@slug", slug);
            command.Parameters.AddWithValue("@drafts", includeDrafts);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetGuid(0);
            name = reader.GetString(1);
            status = reader.GetByte(2);
            showLiveResults = reader.GetBoolean(3);
            shuffleContests = reader.GetBoolean(4);
            shuffleChoices = reader.GetBoolean(5);
            isDemo = reader.GetBoolean(6);
        }

        List<(Guid Id, string Title, int Seats, int MaxSelections, bool AllowWriteIn)> contests = [];
        await using (SqlCommand command = new("""
            SELECT Id, Title, Seats, MaxSelections, AllowWriteIn
            FROM dbo.Contests
            WHERE ElectionId = @e
            ORDER BY SortOrder
            """, connection))
        {
            command.Parameters.AddWithValue("@e", id);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                contests.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetBoolean(4)));
            }
        }

        Dictionary<Guid, List<ChoiceView>> choices = [];
        await using (SqlCommand command = new("""
            SELECT ch.ContestId, ch.Id, ch.Label
            FROM dbo.Choices ch
            JOIN dbo.Contests c ON c.Id = ch.ContestId
            WHERE c.ElectionId = @e
            ORDER BY ch.SortOrder
            """, connection))
        {
            command.Parameters.AddWithValue("@e", id);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid contestId = reader.GetGuid(0);
                if (!choices.TryGetValue(contestId, out List<ChoiceView>? list))
                {
                    list = [];
                    choices[contestId] = list;
                }

                list.Add(new ChoiceView(reader.GetGuid(1), reader.GetString(2)));
            }
        }

        List<ContestView> views = contests
            .Select(c => new ContestView(c.Id, c.Title, c.Seats, c.MaxSelections, c.AllowWriteIn, choices.TryGetValue(c.Id, out List<ChoiceView>? list) ? list : []))
            .ToList();

        return new ElectionView(id, slug, name, status, showLiveResults, shuffleContests, shuffleChoices, isDemo, views);
    }

    public async Task<IReadOnlyList<ElectionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new("SELECT Id, Slug, Name, Status, IsDemo, CodesIssued FROM dbo.Elections ORDER BY Name", connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<ElectionSummary> elections = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            elections.Add(new ElectionSummary(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3), reader.GetBoolean(4), reader.GetInt32(5)));
        }

        return elections;
    }

    public async Task<TurnoutView?> GetTurnoutAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new("dbo.GetTurnout", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@ElectionId", electionId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TurnoutView(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }
}
