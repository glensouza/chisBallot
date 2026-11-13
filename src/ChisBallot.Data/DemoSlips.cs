using Npgsql;

namespace ChisBallot.Data;

/// <summary>
/// Hands out a slip for a demo election only. The database function refuses
/// any election that is not an open demo.
/// </summary>
public sealed class DemoSlips(string connectionString)
{
    public async Task<string?> IssueAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        string code = VotingCode.Generate();

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("SELECT ballot.issue_demo_code(@election, @hash)", connection);
        command.Parameters.AddWithValue("election", electionId);
        command.Parameters.AddWithValue("hash", VotingCode.Hash(code));

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))! ? code : null;
    }
}
