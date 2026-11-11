using System.Data;
using Microsoft.Data.SqlClient;
using Serilog;

namespace ChisBallot.Data;

/// <summary>
/// Casts a ballot through the dbo.CastBallot procedure, which consumes the
/// code and inserts the ballot in one transaction. Never logs the code, a
/// hash of it, the choices, or anything about who is calling.
/// </summary>
public sealed class BallotCaster(string connectionString, ILogger logger)
{
    private const int MaxReceiptAttempts = 5;
    private const int UniqueViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async Task<CastOutcome> CastAsync(
        Guid electionId,
        string code,
        IReadOnlyList<BallotSelection> selections,
        IReadOnlyList<WriteInEntry> writeIns,
        CancellationToken cancellationToken = default)
    {
        DataTable selectionTable = new();
        selectionTable.Columns.Add("ContestId", typeof(Guid));
        selectionTable.Columns.Add("ChoiceId", typeof(Guid));
        HashSet<BallotSelection> seenSelections = [];
        foreach (BallotSelection selection in selections)
        {
            if (!seenSelections.Add(selection))
            {
                return this.Refused(electionId);
            }

            selectionTable.Rows.Add(selection.ContestId, selection.ChoiceId);
        }

        DataTable writeInTable = new();
        writeInTable.Columns.Add("ContestId", typeof(Guid));
        writeInTable.Columns.Add("Name", typeof(string));
        writeInTable.Columns.Add("NameKey", typeof(string));
        HashSet<(Guid, string)> seenWriteIns = [];
        foreach (WriteInEntry writeIn in writeIns)
        {
            if (!WriteInName.TryNormalize(writeIn.Name, out string display, out string key) || !seenWriteIns.Add((writeIn.ContestId, key)))
            {
                return this.Refused(electionId);
            }

            writeInTable.Rows.Add(writeIn.ContestId, display, key);
        }

        byte[] codeHash = VotingCode.Hash(code);

        for (int attempt = 0; attempt < MaxReceiptAttempts; attempt++)
        {
            string receipt = ReceiptCode.Generate();

            try
            {
                await using SqlConnection connection = new(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqlCommand command = new("dbo.CastBallot", connection) { CommandType = CommandType.StoredProcedure };
                command.Parameters.Add(new SqlParameter("@ElectionId", SqlDbType.UniqueIdentifier) { Value = electionId });
                command.Parameters.Add(new SqlParameter("@CodeHash", SqlDbType.Binary, 32) { Value = codeHash });
                command.Parameters.Add(new SqlParameter("@ReceiptCode", SqlDbType.Char, 11) { Value = receipt });
                command.Parameters.Add(new SqlParameter("@Selections", SqlDbType.Structured) { TypeName = "dbo.SelectionList", Value = selectionTable });
                command.Parameters.Add(new SqlParameter("@WriteIns", SqlDbType.Structured) { TypeName = "dbo.WriteInList", Value = writeInTable });
                SqlParameter result = new("@Result", SqlDbType.Int) { Direction = ParameterDirection.Output };
                command.Parameters.Add(result);

                await command.ExecuteNonQueryAsync(cancellationToken);

                CastStatus status = (int)result.Value switch
                {
                    0 => CastStatus.Cast,
                    1 => CastStatus.CodeRefused,
                    _ => CastStatus.SelectionsRefused,
                };

                logger.Information("Ballot cast attempt for election {ElectionId} finished with {Status}", electionId, status);
                return new CastOutcome(status, status == CastStatus.Cast ? receipt : null);
            }
            catch (SqlException exception) when (exception.Number is UniqueViolation or UniqueIndexViolation)
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
