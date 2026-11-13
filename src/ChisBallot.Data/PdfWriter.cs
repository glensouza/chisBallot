namespace ChisBallot.Data;

using System.Globalization;
using System.Text;

/// <summary>
/// A very small PDF writer: Letter pages, text in the three built-in fonts and straight
/// lines. Text is written as hexadecimal Windows-1252 strings, so the file is plain ASCII.
/// It exists so the signed record needs no library and no fonts on the server.
/// </summary>
internal sealed class PdfWriter
{
    public const double PageWidth = 612;
    public const double PageHeight = 792;

    private readonly List<StringBuilder> pages = [];
    private int current = -1;

    public int PageCount => this.pages.Count;

    public void NewPage()
    {
        this.pages.Add(new StringBuilder());
        this.current = this.pages.Count - 1;
    }

    public void GoToPage(int index) => this.current = index;

    /// <summary>Draws text with its left edge at x and its baseline at y (points from the bottom-left).</summary>
    public void Text(double x, double y, string text, PdfFont font, double size)
    {
        string name = font switch
        {
            PdfFont.HelveticaBold => "F2",
            PdfFont.Courier => "F3",
            _ => "F1",
        };

        this.pages[this.current]
            .Append("BT /").Append(name).Append(' ').Append(Number(size)).Append(" Tf ")
            .Append(Number(x)).Append(' ').Append(Number(y)).Append(" Td <")
            .Append(Convert.ToHexString(HelveticaMetrics.Encode(text))).AppendLine("> Tj ET");
    }

    public void Line(double x1, double y1, double x2, double y2, double width)
    {
        this.pages[this.current]
            .Append(Number(width)).Append(" w ")
            .Append(Number(x1)).Append(' ').Append(Number(y1)).Append(" m ")
            .Append(Number(x2)).Append(' ').Append(Number(y2)).AppendLine(" l S");
    }

    public byte[] ToBytes(string title)
    {
        // Objects: 1 catalog, 2 page tree, 3-5 fonts, 6 document info, then a page and its content for each page.
        List<string> objects = [];
        string kids = string.Join(' ', Enumerable.Range(0, this.pages.Count).Select(i => $"{7 + (i * 2)} 0 R"));
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {this.pages.Count} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        objects.Add($"<< /Title <{Convert.ToHexString(HelveticaMetrics.Encode(title))}> /Producer (chisBallot) >>");
        foreach (StringBuilder page in this.pages)
        {
            int content = objects.Count + 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(PageWidth)} {Number(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {content} 0 R >>");
            string stream = page.ToString();
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
        }

        StringBuilder file = new("%PDF-1.4\n");
        List<int> offsets = [];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Length);
            file.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xref = file.Length;
        file.Append("xref\n0 ").Append(objects.Count + 1).Append('\n').Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            file.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        file.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R /Info 6 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(file.ToString());
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
