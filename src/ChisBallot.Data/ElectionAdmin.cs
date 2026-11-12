using ChisBallot.Contracts;
using Microsoft.Data.SqlClient;
using Serilog;

namespace ChisBallot.Data;

/// <summary>
/// What an admin can do to an election: create a draft, open it, close it.
/// Connects as the admin database user, which has no right to read ballots, so
/// nothing here can look at how anyone voted.
/// </summary>
public sealed class ElectionAdmin(string connectionString, ILogger logger)
{
    private const int UniqueViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const int ElectionChangeRefused = 50002;
    private const int NotOpen = 50003;

    public async Task<CreateElectionResponse> CreateAsync(CreateElectionRequest request, CancellationToken cancellationToken = default)
    {
        string? problem = ElectionRequestValidator.Validate(request);
        if (problem is not null)
        {
            return new CreateElectionResponse(null, problem);
        }

        Guid electionId = Guid.NewGuid();

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.Elections (Id, Slug, Name, Status, CodesIssued, ShowLiveResults, ShuffleContests, ShuffleChoices, IsDemo)
                VALUES (@id, @slug, @name, 0, 0, @live, @shuffleContests, @shuffleChoices, 0)
                """,
                ("@id", electionId), ("@slug", request.Slug), ("@name", request.Name.Trim()), ("@live", request.ShowLiveResults),
                ("@shuffleContests", request.ShuffleContests), ("@shuffleChoices", request.ShuffleChoices));

            for (int contestIndex = 0; contestIndex < request.Contests.Count; contestIndex++)
            {
                ContestInput contest = request.Contests[contestIndex];
                Guid contestId = Guid.NewGuid();
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Contests (Id, ElectionId, Title, Seats, MaxSelections, AllowWriteIn, SortOrder)
                    VALUES (@id, @election, @title, @seats, @max, @writeIn, @order)
                    """,
                    ("@id", contestId), ("@election", electionId), ("@title", contest.Title.Trim()), ("@seats", contest.Seats),
                    ("@max", contest.MaxSelections), ("@writeIn", contest.AllowWriteIn), ("@order", contestIndex));

                for (int choiceIndex = 0; choiceIndex < contest.Choices.Count; choiceIndex++)
                {
                    await ExecuteAsync(connection, transaction,
                        "INSERT dbo.Choices (Id, ContestId, Label, SortOrder) VALUES (NEWID(), @contest, @label, @order)",
                        ("@contest", contestId), ("@label", contest.Choices[choiceIndex].Trim()), ("@order", choiceIndex));
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is UniqueViolation or UniqueIndexViolation)
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

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = connection.BeginTransaction();

        // Claiming the draft and writing the hashes happen together, so slips can be made only once.
        await using (SqlCommand claim = new("UPDATE dbo.Elections SET CodesIssued = @n WHERE Id = @id AND Status = 0 AND CodesIssued = 0", connection, transaction))
        {
            claim.Parameters.AddWithValue("@n", count);
            claim.Parameters.AddWithValue("@id", electionId);
            if (await claim.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return null;
            }
        }

        List<string> codes = [];
        for (int index = 0; index < count; index++)
        {
            string code = VotingCode.Generate();
            codes.Add(code);
            await ExecuteAsync(connection, transaction, "INSERT dbo.VotingCodes (ElectionId, CodeHash) VALUES (@e, @h)",
                ("@e", electionId), ("@h", VotingCode.Hash(code)));
        }

        await transaction.CommitAsync(cancellationToken);
        logger.Information("Made {SlipCount} slips for election {ElectionId}", count, electionId);
        return codes;
    }

    /// <summary>Opens a draft that has slips. Returns false when it is not a draft or has no slips yet.</summary>
    public async Task<bool> OpenAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new("UPDATE dbo.Elections SET Status = 1 WHERE Id = @id AND Status = 0 AND CodesIssued > 0", connection);
        command.Parameters.AddWithValue("@id", electionId);

        try
        {
            int changed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 0)
            {
                logger.Information("Opened election {ElectionId}", electionId);
            }

            return changed != 0;
        }
        catch (SqlException exception) when (exception.Number == ElectionChangeRefused)
        {
            return false;
        }
    }

    /// <summary>Closes an open election and deletes its unused slips. Returns false when it is not open.</summary>
    public async Task<bool> CloseAsync(Guid electionId, CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new("dbo.CloseElection", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@ElectionId", electionId);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            logger.Information("Closed election {ElectionId}", electionId);
            return true;
        }
        catch (SqlException exception) when (exception.Number == NotOpen)
        {
            return false;
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
