namespace ChisBallot.Data;

using ChisBallot.Contracts;
using Npgsql;

/// <summary>
/// Reads the published ballot list through ballot.get_ballots, which the
/// database answers only for a Closed election. Anything earlier is null.
/// </summary>
public sealed class AuditReader(string connectionString)
{
    private const string NotAvailable = "CB001";

    public async Task<IReadOnlyList<PublishedBallot>?> GetBallotsAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        List<PublishedBallot> ballots = [];
        string? receipt = null;
        List<PublishedSelection> lines = [];

        try
        {
            await using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = new("SELECT receipt_code, contest_id, choice_id, write_in FROM ballot.get_ballots(@e)", connection);
            command.Parameters.AddWithValue("e", electionId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                string current = reader.GetString(0);
                if (receipt is not null && current != receipt)
                {
                    ballots.Add(new PublishedBallot(receipt, lines));
                    lines = [];
                }

                receipt = current;
                if (!reader.IsDBNull(1))
                {
                    lines.Add(new PublishedSelection(
                        reader.GetGuid(1),
                        reader.IsDBNull(2) ? null : reader.GetGuid(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
            }
        }
        catch (PostgresException exception) when (exception.SqlState == NotAvailable)
        {
            return null;
        }

        if (receipt is not null)
        {
            ballots.Add(new PublishedBallot(receipt, lines));
        }

        return ballots;
    }
}
