using ChisBallot.Contracts;
using Microsoft.Data.SqlClient;

namespace ChisBallot.Data;

/// <summary>
/// Reads vote counts through dbo.GetResults, which the database only answers
/// once the election is Closed (or while Open, if the election turned live
/// results on). Anything earlier comes back as null.
/// </summary>
public sealed class ResultsReader(string connectionString)
{
    private const int ResultsNotAvailable = 50001;

    public async Task<IReadOnlyList<ContestResult>?> GetAsync(ElectionView election, CancellationToken cancellationToken = default)
    {
        Dictionary<Guid, List<(string Label, int Votes, bool IsWriteIn)>> counts = [];
        Dictionary<Guid, string> labels = election.Contests.SelectMany(c => c.Choices).ToDictionary(choice => choice.Id, choice => choice.Label);

        try
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = new("dbo.GetResults", connection) { CommandType = System.Data.CommandType.StoredProcedure };
            command.Parameters.AddWithValue("@ElectionId", election.Id);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                Guid contestId = reader.GetGuid(0);
                int votes = reader.GetInt32(3);
                bool isChoice = !reader.IsDBNull(1);

                // While Open the database returns write-ins as one combined row with no name.
                string label = isChoice
                    ? labels[reader.GetGuid(1)]
                    : reader.IsDBNull(2) ? "Write-ins (combined)" : reader.GetString(2);

                if (!counts.TryGetValue(contestId, out List<(string, int, bool)>? list))
                {
                    list = [];
                    counts[contestId] = list;
                }

                list.Add((label, votes, !isChoice));
            }
        }
        catch (SqlException exception) when (exception.Number == ResultsNotAvailable)
        {
            return null;
        }

        return
        [
            .. election.Contests.Select(contest => ResultsCalculator.Rank(
                contest.Id,
                contest.Title,
                contest.Seats,
                counts.TryGetValue(contest.Id, out List<(string Label, int Votes, bool IsWriteIn)>? list) ? list : [])),
        ];
    }
}
