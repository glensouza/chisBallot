using Npgsql;
using NpgsqlTypes;
using Serilog;

namespace ChisBallot.Data;

/// <summary>
/// Casts a ballot through ballot.cast_ballot, which consumes the code and
/// inserts the ballot in one transaction. Never logs the code, a hash of it,
/// the choices, or anything about who is calling.
/// </summary>
public sealed class BallotCaster(string connectionString, ILogger logger)
{
    private const int MaxReceiptAttempts = 5;
    private const string UniqueViolation = "23505";

    public async Task<CastOutcome> CastAsync(
        Guid electionId,
        string code,
        IReadOnlyList<BallotSelection> selections,
        IReadOnlyList<WriteInEntry> writeIns,
        CancellationToken cancellationToken = default)
    {
        List<Guid> selectionContests = [];
        List<Guid> selectionChoices = [];
        HashSet<BallotSelection> seenSelections = [];
        foreach (BallotSelection selection in selections)
        {
            if (!seenSelections.Add(selection))
            {
                return this.Refused(electionId);
            }

            selectionContests.Add(selection.ContestId);
            selectionChoices.Add(selection.ChoiceId);
        }

        List<Guid> writeInContests = [];
        List<string> writeInNames = [];
        List<string> writeInKeys = [];
        HashSet<(Guid, string)> seenWriteIns = [];
        foreach (WriteInEntry writeIn in writeIns)
        {
            if (!WriteInName.TryNormalize(writeIn.Name, out string display, out string key) || !seenWriteIns.Add((writeIn.ContestId, key)))
            {
                return this.Refused(electionId);
            }

            writeInContests.Add(writeIn.ContestId);
            writeInNames.Add(display);
            writeInKeys.Add(key);
        }

        byte[] codeHash = VotingCode.Hash(code);

        for (int attempt = 0; attempt < MaxReceiptAttempts; attempt++)
        {
            string receipt = ReceiptCode.Generate();

            try
            {
                await using NpgsqlConnection connection = new(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using NpgsqlCommand command = new("SELECT ballot.cast_ballot(@election, @hash, @receipt, @selContests, @selChoices, @wiContests, @wiNames, @wiKeys)", connection);
                command.Parameters.Add(new NpgsqlParameter("election", NpgsqlDbType.Uuid) { Value = electionId });
                command.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = codeHash });
                command.Parameters.Add(new NpgsqlParameter("receipt", NpgsqlDbType.Text) { Value = receipt });
                command.Parameters.Add(new NpgsqlParameter("selContests", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = selectionContests.ToArray() });
                command.Parameters.Add(new NpgsqlParameter("selChoices", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = selectionChoices.ToArray() });
                command.Parameters.Add(new NpgsqlParameter("wiContests", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = writeInContests.ToArray() });
                command.Parameters.Add(new NpgsqlParameter("wiNames", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = writeInNames.ToArray() });
                command.Parameters.Add(new NpgsqlParameter("wiKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = writeInKeys.ToArray() });

                CastStatus status = (int)(await command.ExecuteScalarAsync(cancellationToken))! switch
                {
                    0 => CastStatus.Cast,
                    1 => CastStatus.CodeRefused,
                    _ => CastStatus.SelectionsRefused,
                };

                logger.Information("Ballot cast attempt for election {ElectionId} finished with {Status}", electionId, status);
                return new CastOutcome(status, status == CastStatus.Cast ? receipt : null);
            }
            catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
            {
                // The receipt code already exists in this election. The
                // transaction rolled back, so the code is still unused: try
                // again with a new receipt.
                continue;
            }
        }

        throw new InvalidOperationException("Could not generate an unused receipt code.");
    }

    private CastOutcome Refused(Guid electionId)
    {
        logger.Information("Ballot cast attempt for election {ElectionId} finished with {Status}", electionId, CastStatus.SelectionsRefused);
        return new CastOutcome(CastStatus.SelectionsRefused, null);
    }
}
