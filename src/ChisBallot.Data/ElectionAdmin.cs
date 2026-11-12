using ChisBallot.Contracts;
using Npgsql;
using NpgsqlTypes;
using Serilog;

namespace ChisBallot.Data;

/// <summary>
/// What an admin can do to an election: create a draft, make its slips, open
/// it, close it. Connects as the admin database role, which has no right to
/// read ballots, so nothing here can look at how anyone voted.
/// </summary>
public sealed class ElectionAdmin(string connectionString, ILogger logger)
{
    private const string UniqueViolation = "23505";
    private const string ElectionChangeRefused = "CB002";
    private const string NotOpen = "CB003";

    public async Task<CreateElectionResponse> CreateAsync(CreateElectionRequest request, CancellationToken cancellationToken = default)
    {
        string? problem = ElectionRequestValidator.Validate(request);
        if (problem is not null)
        {
            return new CreateElectionResponse(null, problem);
        }

        Guid electionId = Guid.NewGuid();

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results, shuffle_contests, shuffle_choices, is_demo)
                VALUES (@id, @slug, @name, 0, 0, @live, @shuffleContests, @shuffleChoices, false)
                """,
                ("id", electionId), ("slug", request.Slug), ("name", request.Name.Trim()), ("live", request.ShowLiveResults),
                ("shuffleContests", request.ShuffleContests), ("shuffleChoices", request.ShuffleChoices));

            for (int contestIndex = 0; contestIndex < request.Contests.Count; contestIndex++)
            {
                ContestInput contest = request.Contests[contestIndex];
                Guid contestId = Guid.NewGuid();
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order)
                    VALUES (@id, @election, @title, @seats, @max, @writeIn, @order)
                    """,
                    ("id", contestId), ("election", electionId), ("title", contest.Title.Trim()), ("seats", contest.Seats),
                    ("max", contest.MaxSelections), ("writeIn", contest.AllowWriteIn), ("order", contestIndex));

                for (int choiceIndex = 0; choiceIndex < contest.Choices.Count; choiceIndex++)
                {
                    await ExecuteAsync(connection, transaction,
                        "INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), @contest, @label, @order)",
                        ("contest", contestId), ("label", contest.Choices[choiceIndex].Trim()), ("order", choiceIndex));
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return new CreateElectionResponse(null, "That address is already used by another vote.");
        }

        logger.Information("Created draft election {ElectionId}", electionId);
        return new CreateElectionResponse(electionId, null);
    }

    /// <summary>
    /// Makes the slips for a draft that has none. Only the hashes are stored; the
    /// plain codes are returned once and are not logged. Returns null when the
    /// election is not a draft, already has slips, or the count is out of range.
    /// </summary>
    public async Task<IReadOnlyList<string>?> IssueSlipsAsync(Guid electionId, int count, CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > SlipLimits.Max)
        {
            return null;
        }

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Claiming the draft and writing the hashes happen together, so slips can be made only once.
        await using (NpgsqlCommand claim = new("UPDATE ballot.elections SET codes_issued = @n WHERE id = @id AND status = 0 AND codes_issued = 0", connection, transaction))
        {
            claim.Parameters.AddWithValue("n", count);
            claim.Parameters.AddWithValue("id", electionId);
            if (await claim.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return null;
            }
        }

        List<string> codes = [];
        List<byte[]> hashes = [];
        for (int index = 0; index < count; index++)
        {
            string code = VotingCode.Generate();
            codes.Add(code);
            hashes.Add(VotingCode.Hash(code));
        }

        await using (NpgsqlCommand insert = new("INSERT INTO ballot.voting_codes (election_id, code_hash) SELECT @e, unnest(@hashes)", connection, transaction))
        {
            insert.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.Uuid) { Value = electionId });
            insert.Parameters.Add(new NpgsqlParameter("hashes", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = hashes.ToArray() });
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.Information("Made {SlipCount} slips for election {ElectionId}", count, electionId);
        return codes;
    }

    /// <summary>Opens a draft that has slips. Returns false when it is not a draft or has no slips yet.</summary>
    public async Task<bool> OpenAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("UPDATE ballot.elections SET status = 1 WHERE id = @id AND status = 0 AND codes_issued > 0", connection);
        command.Parameters.AddWithValue("id", electionId);

        try
        {
            int changed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 0)
            {
                logger.Information("Opened election {ElectionId}", electionId);
            }

            return changed != 0;
        }
        catch (PostgresException exception) when (exception.SqlState == ElectionChangeRefused)
        {
            return false;
        }
    }

    /// <summary>Closes an open election and deletes its unused slips. Returns false when it is not open.</summary>
    public async Task<bool> CloseAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("SELECT ballot.close_election(@id)", connection);
        command.Parameters.AddWithValue("id", electionId);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            logger.Information("Closed election {ElectionId}", electionId);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == NotOpen)
        {
            return false;
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection, transaction);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
