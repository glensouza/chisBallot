namespace ChisBallot.Data;

using ChisBallot.Contracts;
using System.Globalization;

/// <summary>
/// The printable record of a closed vote: the result of every question, the audit numbers,
/// a fingerprint of the published ballots and lines for the board chair and two witnesses
/// to sign. It holds nothing that is not already public on the results and audit pages.
/// </summary>
public sealed class ResultsRecord
{
    private const double Left = 54;
    private const double Right = 558;
    private const double Top = 738;
    private const double Bottom = 66;

    private readonly PdfWriter pdf = new();
    private double y = Top;

    private ResultsRecord()
    {
    }

    public static byte[] Build(
        ElectionView election,
        IReadOnlyList<ContestResult> results,
        TurnoutView turnout,
        string fingerprint,
        string? commit,
        string auditAddress,
        DateOnly printedOn)
    {
        ResultsRecord record = new();
        record.WriteHeader(election, printedOn);
        foreach (ContestResult contest in results)
        {
            record.WriteContest(contest);
        }

        record.WriteAuditNumbers(turnout);
        record.WriteHowToCheck(fingerprint, commit, auditAddress);
        record.WriteSignatures();
        record.WriteFooters(election.Slug);
        return record.pdf.ToBytes("Record of results: " + election.Name);
    }

    private static List<string> Wrap(string text, PdfFont font, double size, double width)
    {
        List<string> lines = [];
        string line = string.Empty;
        foreach (string word in text.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (HelveticaMetrics.Width(candidate, font, size) <= width)
            {
                line = candidate;
                continue;
            }

            if (line.Length != 0)
            {
                lines.Add(line);
                line = string.Empty;
            }

            // One word wider than the line is cut where it no longer fits.
            string rest = word;
            while (HelveticaMetrics.Width(rest, font, size) > width)
            {
                int cut = rest.Length - 1;
                while (cut > 1 && HelveticaMetrics.Width(rest[..cut], font, size) > width)
                {
                    cut--;
                }

                lines.Add(rest[..cut]);
                rest = rest[cut..];
            }

            line = rest;
        }

        if (line.Length != 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static string Seats(int count) => count == 1 ? "1 seat" : count.ToString(CultureInfo.InvariantCulture) + " seats";

    private void Ensure(double needed)
    {
        if (this.pdf.PageCount != 0 && this.y - needed >= Bottom)
        {
            return;
        }

        this.pdf.NewPage();
        this.y = Top;
    }

    private void Paragraph(string text, PdfFont font, double size, double leading, double x = Left, double width = Right - Left)
    {
        foreach (string line in Wrap(text, font, size, width))
        {
            this.Ensure(leading);
            this.pdf.Text(x, this.y - size, line, font, size);
            this.y -= leading;
        }
    }

    private void Rule(double gap)
    {
        this.Ensure(gap);
        this.pdf.Line(Left, this.y - (gap / 2), Right, this.y - (gap / 2), 0.5);
        this.y -= gap;
    }

    private void WriteHeader(ElectionView election, DateOnly printedOn)
    {
        this.Ensure(0);
        if (election.IsDemo)
        {
            this.Paragraph("DEMO: NOT A REAL VOTE", PdfFont.HelveticaBold, 16, 24);
        }

        this.Paragraph("Record of results", PdfFont.HelveticaBold, 22, 30);
        this.Paragraph(election.Name, PdfFont.HelveticaBold, 15, 21);
        this.Paragraph("Citrus Hills Intermediate School council", PdfFont.Helvetica, 11, 16);
        this.Paragraph("Printed " + printedOn.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture), PdfFont.Helvetica, 10, 16);
        this.Rule(18);
    }

    private void WriteContest(ContestResult contest)
    {
        this.Ensure(80);
        this.Paragraph(contest.Title, PdfFont.HelveticaBold, 13, 18);
        this.Paragraph(Seats(contest.Seats), PdfFont.Helvetica, 9, 14);
        this.y -= 2;

        foreach (ResultEntry entry in contest.Entries)
        {
            string label = entry.IsWriteIn ? entry.Label + " (write-in)" : entry.Label;
            if (entry.IsWinner)
            {
                label += "  (elected)";
            }

            PdfFont font = entry.IsWinner ? PdfFont.HelveticaBold : PdfFont.Helvetica;
            string votes = entry.Votes.ToString(CultureInfo.InvariantCulture);
            List<string> lines = Wrap(label, font, 11, Right - Left - 60);
            this.Ensure(16 * Math.Max(1, lines.Count));
            this.pdf.Text(Right - HelveticaMetrics.Width(votes, font, 11), this.y - 11, votes, font, 11);
            foreach (string line in lines)
            {
                this.pdf.Text(Left + 8, this.y - 11, line, font, 11);
                this.y -= 16;
            }
        }

        if (contest.Tie is not null)
        {
            this.y -= 2;
            this.Paragraph(
                "Tie: revote needed for " + Seats(contest.Tie.OpenSeats) + " between " + string.Join(", ", contest.Tie.TiedLabels) + ". No winner is picked for those seats.",
                PdfFont.HelveticaBold,
                10,
                14,
                Left + 8,
                Right - Left - 8);
        }

        this.Rule(18);
    }

    private void WriteAuditNumbers(TurnoutView turnout)
    {
        this.Ensure(130);
        this.Paragraph("Audit numbers", PdfFont.HelveticaBold, 13, 18);
        (string Label, int Value)[] rows =
        [
            ("Slips issued", turnout.CodesIssued),
            ("Slips used", turnout.CodesUsed),
            ("Unused slips, deleted when the vote closed", turnout.CodesIssued - turnout.CodesUsed),
            ("Ballots counted", turnout.BallotsCounted),
        ];

        foreach ((string label, int value) in rows)
        {
            string number = value.ToString(CultureInfo.InvariantCulture);
            this.Ensure(16);
            this.pdf.Text(Left + 8, this.y - 11, label, PdfFont.Helvetica, 11);
            this.pdf.Text(Right - HelveticaMetrics.Width(number, PdfFont.Helvetica, 11), this.y - 11, number, PdfFont.Helvetica, 11);
            this.y -= 16;
        }

        this.y -= 2;
        string check = turnout.CodesUsed == turnout.BallotsCounted
            ? "Check passed: slips used equals ballots counted."
            : $"Check FAILED: slips used ({turnout.CodesUsed}) does not equal ballots counted ({turnout.BallotsCounted}).";
        this.Paragraph(check, PdfFont.HelveticaBold, 10, 14, Left + 8, Right - Left - 8);
        this.Rule(18);
    }

    private void WriteHowToCheck(string fingerprint, string? commit, string auditAddress)
    {
        this.Ensure(130);
        this.Paragraph("How to check this record", PdfFont.HelveticaBold, 13, 18);
        this.Paragraph("Every anonymous ballot, the list of receipt codes and a recount in the browser are public at:", PdfFont.Helvetica, 10, 14);
        this.Paragraph(auditAddress, PdfFont.Courier, 9, 14);
        this.y -= 4;
        this.Paragraph("Fingerprint of the published ballot list (SHA-256). The audit page shows the same value:", PdfFont.Helvetica, 10, 14);
        this.Paragraph(fingerprint, PdfFont.Courier, 9, 14);
        if (commit is not null)
        {
            this.y -= 4;
            this.Paragraph("The site was built from commit:", PdfFont.Helvetica, 10, 14);
            this.Paragraph(commit, PdfFont.Courier, 9, 14);
        }

        this.Rule(18);
    }

    private void WriteSignatures()
    {
        this.Ensure(270);
        this.Paragraph("Signatures", PdfFont.HelveticaBold, 13, 18);
        this.Paragraph("The undersigned confirm that this record shows the result of the vote named above, as published by chisBallot.", PdfFont.Helvetica, 10, 14);
        this.y -= 6;

        foreach (string role in new[] { "Board chair", "Witness 1", "Witness 2" })
        {
            this.Ensure(64);
            this.pdf.Text(Left, this.y - 11, role, PdfFont.HelveticaBold, 11);
            double line = this.y - 44;
            this.pdf.Line(Left, line, 300, line, 0.6);
            this.pdf.Line(315, line, 470, line, 0.6);
            this.pdf.Line(485, line, Right, line, 0.6);
            this.pdf.Text(Left, line - 10, "Signature", PdfFont.Helvetica, 8);
            this.pdf.Text(315, line - 10, "Printed name", PdfFont.Helvetica, 8);
            this.pdf.Text(485, line - 10, "Date", PdfFont.Helvetica, 8);
            this.y -= 68;
        }

        this.Paragraph("Paper ballots, if they were used alongside the site, are counted and recorded separately.", PdfFont.Helvetica, 9, 13);
    }

    private void WriteFooters(string slug)
    {
        int count = this.pdf.PageCount;
        for (int page = 0; page < count; page++)
        {
            this.pdf.GoToPage(page);
            this.pdf.Text(Left, 36, $"chisBallot record: {slug}", PdfFont.Helvetica, 8);
            string number = $"Page {page + 1} of {count}";
            this.pdf.Text(Right - HelveticaMetrics.Width(number, PdfFont.Helvetica, 8), 36, number, PdfFont.Helvetica, 8);
        }
    }
}
