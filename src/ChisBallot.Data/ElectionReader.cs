using ChisBallot.Contracts;
using Npgsql;

namespace ChisBallot.Data;

/// <summary>
/// Reads the parts of an election that are not secret: what the ballot page
/// shows, the admin list, and turnout. Never touches ballots; the database
/// roles have no right to.
/// </summary>
public sealed class ElectionReader(string connectionString)
{
    public async Task<ElectionView?> FindBySlugAsync(string slug, bool includeDrafts, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        Guid id;
        string name;
        int status;
        bool showLiveResults;
        bool shuffleContests;
        bool shuffleChoices;
        bool isDemo;

        await using (NpgsqlCommand command = new("""
            SELECT id, name, status, show_live_results, shuffle_contests, shuffle_choices, is_demo
            FROM ballot.elections
            WHERE slug = @slug AND (@drafts OR status <> 0)
            """, connection))
        {
            command.Parameters.AddWithValue("slug", slug);
            command.Parameters.AddWithValue("drafts", includeDrafts);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetGuid(0);
            name = reader.GetString(1);
            status = reader.GetInt16(2);
            showLiveResults = reader.GetBoolean(3);
            shuffleContests = reader.GetBoolean(4);
            shuffleChoices = reader.GetBoolean(5);
            isDemo = reader.GetBoolean(6);
        }

        List<(Guid Id, string Title, int Seats, int MaxSelections, bool AllowWriteIn)> contests = [];
        await using (NpgsqlCommand command = new("""
            SELECT id, title, seats, max_selections, allow_write_in
            FROM ballot.contests
            WHERE election_id = @e
            ORDER BY sort_order
            """, connection))
        {
            command.Parameters.AddWithValue("e", id);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                contests.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetBoolean(4)));
            }
        }

        Dictionary<Guid, List<ChoiceView>> choices = [];
        await using (NpgsqlCommand command = new("""
            SELECT ch.contest_id, ch.id, ch.label
            FROM ballot.choices ch
            JOIN ballot.contests c ON c.id = ch.contest_id
            WHERE c.election_id = @e
            ORDER BY ch.sort_order
            """, connection))
        {
            command.Parameters.AddWithValue("e", id);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
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
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("SELECT id, slug, name, status, is_demo, codes_issued FROM ballot.elections ORDER BY name", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<ElectionSummary> elections = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            elections.Add(new ElectionSummary(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetBoolean(4), reader.GetInt32(5)));
        }

        return elections;
    }

    public async Task<TurnoutView?> GetTurnoutAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("SELECT codes_issued, codes_used, ballots_counted FROM ballot.get_turnout(@e)", connection);
        command.Parameters.AddWithValue("e", electionId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TurnoutView(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }
}
