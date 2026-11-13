namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public sealed partial class ResultsRecordTests
{
    private static ElectionView Election(string name = "Spring council vote", bool isDemo = false) =>
        new(Guid.NewGuid(), "spring", name, Status: 2, ShowLiveResults: false, ShuffleContests: false, ShuffleChoices: false, isDemo, []);

    private static byte[] Build(IReadOnlyList<ContestResult> results, ElectionView? election = null, TurnoutView? turnout = null, string? commit = "abc123def456") =>
        ResultsRecord.Build(
            election ?? Election(),
            results,
            turnout ?? new TurnoutView(40, 31, 31),
            new string('a', 64),
            commit,
            "https://vote.chisballot.org/audit/spring",
            new DateOnly(2026, 11, 13));

    private static ContestResult Contest(string title, int seats, params ResultEntry[] entries) =>
        new(Guid.NewGuid(), title, seats, entries, Tie: null);

    [GeneratedRegex("<([0-9A-F]*)> Tj")]
    private static partial Regex ShownText();

    // The text of every string drawn on the pages, decoded the way the writer encoded it.
    private static string TextOf(byte[] pdf)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding windows = Encoding.GetEncoding(1252);
        string file = Encoding.ASCII.GetString(pdf);
        return string.Join('\n', ShownText().Matches(file).Select(m => windows.GetString(Convert.FromHexString(m.Groups[1].Value))));
    }

    [Fact]
    public void The_record_is_a_well_formed_pdf_whose_offsets_point_at_their_objects()
    {
        byte[] pdf = Build([Contest("Chair", 1, new ResultEntry("Ana Ruiz", 20, false, true), new ResultEntry("Sam Lee", 11, false, false))]);
        string file = Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4\n", file, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", file, StringComparison.Ordinal);

        int startXref = int.Parse(file[(file.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], CultureInfo.InvariantCulture);
        Assert.Equal("xref", file.Substring(startXref, 4));

        string[] entries = file[startXref..].Split('\n').Skip(3).TakeWhile(line => line.EndsWith(" n ", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(entries);
        for (int i = 0; i < entries.Length; i++)
        {
            int offset = int.Parse(entries[i][..10], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj", file[offset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_record_shows_results_winners_audit_numbers_fingerprint_and_signature_lines()
    {
        string text = TextOf(Build([Contest("Chair", 1, new ResultEntry("Ana Ruiz", 20, false, true), new ResultEntry("Pat Lee", 3, true, false))]));

        Assert.Contains("Record of results", text, StringComparison.Ordinal);
        Assert.Contains("Spring council vote", text, StringComparison.Ordinal);
        Assert.Contains("Ana Ruiz (elected)", text, StringComparison.Ordinal);
        Assert.Contains("Pat Lee (write-in)", text, StringComparison.Ordinal);
        Assert.Contains("Check passed: slips used equals ballots counted.", text, StringComparison.Ordinal);
        Assert.Contains(new string('a', 64), text, StringComparison.Ordinal);
        Assert.Contains("abc123def456", text, StringComparison.Ordinal);
        Assert.Contains("Board chair", text, StringComparison.Ordinal);
        Assert.Contains("Witness 1", text, StringComparison.Ordinal);
        Assert.Contains("Witness 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DEMO", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tie_is_stated_and_no_winner_is_picked()
    {
        ContestResult tied = new(Guid.NewGuid(), "Seat", 1, [new ResultEntry("A", 5, false, false), new ResultEntry("B", 5, false, false)], new TieInfo(["A", "B"], 1));

        string text = TextOf(Build([tied]));

        Assert.Contains("Tie: revote needed for 1 seat between A, B.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(elected)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_audit_check_is_printed_plainly()
    {
        string text = TextOf(Build([Contest("Q", 1, new ResultEntry("Yes", 1, false, true))], turnout: new TurnoutView(40, 31, 30)));

        Assert.Contains("Check FAILED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_demo_is_marked_on_the_record()
    {
        string text = TextOf(Build([Contest("Q", 1, new ResultEntry("Yes", 1, false, true))], Election(isDemo: true)));

        Assert.Contains("DEMO: NOT A REAL VOTE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Accented_letters_are_kept_and_letters_the_font_lacks_become_question_marks()
    {
        string text = TextOf(Build([Contest("Q", 1, new ResultEntry("José Núñez", 4, false, true), new ResultEntry("李雷", 1, true, false))]));

        Assert.Contains("José Núñez", text, StringComparison.Ordinal);
        Assert.Contains("?? (write-in)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_result_flows_onto_more_pages_and_the_signatures_stay_together()
    {
        ResultEntry[] entries = [.. Enumerable.Range(1, 60).Select(i => new ResultEntry("Choice number " + i, i, false, false))];
        byte[] pdf = Build([Contest("Long one", 1, entries), Contest("Another", 1, entries)]);
        string file = Encoding.ASCII.GetString(pdf);
        string text = TextOf(pdf);

        int pages = Regex.Matches(file, "/Type /Page /Parent").Count;
        Assert.True(pages >= 3, "expected at least three pages but found " + pages);
        Assert.Contains($"Page {pages} of {pages}", text, StringComparison.Ordinal);

        // The "Signatures" heading, the chair and both witnesses are drawn on the same page.
        string[] streams = [.. Regex.Matches(file, "stream\n(.*?)endstream", RegexOptions.Singleline).Select(m => TextOf(Encoding.ASCII.GetBytes(m.Groups[1].Value)))];
        string signaturePage = streams.Single(page => page.Contains("Signatures", StringComparison.Ordinal));
        Assert.Contains("Board chair", signaturePage, StringComparison.Ordinal);
        Assert.Contains("Witness 2", signaturePage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_very_long_name_is_wrapped_instead_of_running_off_the_page()
    {
        string name = new('W', 200);
        byte[] pdf = Build([Contest("Q", 1, new ResultEntry(name, 1, false, true))], Election(name));
        string text = TextOf(pdf);

        Assert.DoesNotContain(name, text, StringComparison.Ordinal);
        Assert.True(text.Count(c => c == 'W') >= 400, "the name should appear in pieces on the record");
    }
}
