namespace ChisBallot.Data;

using Npgsql;

/// <summary>
/// The two demo votes: <c>demo</c>, open with nothing in it so a room can vote, and
/// <c>demo-tie</c>, already closed with made-up ballots that end in a tie. They are made by
/// the database functions in 006_demo_functions.sql, which touch nothing but demos.
/// <see cref="EnsureAsync"/> makes them if they are missing (the migrator does this on every
/// deploy); <see cref="ResetAsync"/> deletes them and any revote of them, and makes them
/// again, so a demo can be run as many times as needed.
/// </summary>
public static class DemoSeed
{
    public const string OpenSlug = "demo";
    public const string TieSlug = "demo-tie";

    public static Task EnsureAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        return CallAsync(connectionString, "ballot.ensure_demos", cancellationToken);
    }

    public static Task ResetAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        return CallAsync(connectionString, "ballot.reset_demos", cancellationToken);
    }

    private static async Task CallAsync(string connectionString, string function, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new($"SELECT {function}()", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
