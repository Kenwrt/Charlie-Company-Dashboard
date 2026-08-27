using System.Net;
using System.Text;
using System.Text.Json;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record ScheduledReportColumn(string Heading, bool AlignRight = false);
public sealed record ScheduledReportRow(IReadOnlyList<string> Cells, bool IsTotal = false);
public sealed record ScheduledReportDocument(
    string ReportName,
    string ReportDate,
    string Description,
    IReadOnlyList<ScheduledReportColumn> Columns,
    IReadOnlyList<ScheduledReportRow> Rows);

public static class ScheduledReportFormatter
{
    private const string Prefix = "CCV_REPORT_V1:";

    public static string Serialize(ScheduledReportDocument document) => Prefix + JsonSerializer.Serialize(document);

    public static bool TryDeserialize(string value, out ScheduledReportDocument? document)
    {
        document = null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            document = JsonSerializer.Deserialize<ScheduledReportDocument>(value[Prefix.Length..]);
            return document is not null;
        }
        catch (JsonException) { return false; }
    }

    public static string ToPlainText(ScheduledReportDocument document)
    {
        var output = new StringBuilder()
            .AppendLine(document.ReportName)
            .AppendLine(document.ReportDate)
            .AppendLine(document.Description)
            .AppendLine()
            .AppendLine(string.Join(" | ", document.Columns.Select(column => column.Heading)))
            .AppendLine(string.Join(" | ", document.Columns.Select(column => new string('-', Math.Max(3, column.Heading.Length)))));
        foreach (var row in document.Rows)
        {
            output.AppendLine(string.Join(" | ", row.Cells));
        }
        return output.ToString().TrimEnd();
    }

    public static string ToHtml(ScheduledReportDocument document)
    {
        static string Encode(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder("<!doctype html><html><body style=\"margin:0;background:#f4f7fb;font-family:Arial,Helvetica,sans-serif;color:#142033\">")
            .Append("<div style=\"max-width:960px;margin:0 auto;padding:28px 16px\">")
            .Append("<div style=\"background:#081f4d;color:#fff;padding:22px 26px;border-radius:10px 10px 0 0\">")
            .Append("<div style=\"font-size:12px;letter-spacing:.08em;text-transform:uppercase;opacity:.8\">Charlie Company Ventures</div>")
            .Append($"<h1 style=\"margin:7px 0 4px;font-size:26px\">{Encode(document.ReportName)}</h1>")
            .Append($"<div style=\"font-size:14px;opacity:.9\">{Encode(document.ReportDate)}</div></div>")
            .Append("<div style=\"background:#fff;border:1px solid #d9e2ef;border-top:0;padding:24px 26px;border-radius:0 0 10px 10px\">")
            .Append($"<p style=\"margin:0 0 18px;color:#52627a\">{Encode(document.Description)}</p>")
            .Append("<div style=\"overflow-x:auto\"><table style=\"width:100%;border-collapse:collapse;font-size:14px\"><thead><tr>");
        foreach (var column in document.Columns)
        {
            html.Append($"<th style=\"padding:11px 12px;background:#eef3f9;border-bottom:2px solid #b8c7da;text-align:{(column.AlignRight ? "right" : "left")};font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:#30435d\">{Encode(column.Heading)}</th>");
        }
        html.Append("</tr></thead><tbody>");
        foreach (var row in document.Rows)
        {
            html.Append(row.IsTotal ? "<tr style=\"background:#eef7f1;font-weight:700\">" : "<tr>");
            for (var index = 0; index < document.Columns.Count; index++)
            {
                var cell = index < row.Cells.Count ? row.Cells[index] : string.Empty;
                html.Append($"<td style=\"padding:11px 12px;border-bottom:1px solid #dfe6ef;vertical-align:top;text-align:{(document.Columns[index].AlignRight ? "right" : "left")}\">{Encode(cell)}</td>");
            }
            html.Append("</tr>");
        }
        return html.Append("</tbody></table></div><p style=\"margin:20px 0 0;color:#758399;font-size:12px\">Generated securely by Charlie Company Ventures. Values reflect synchronized records available when the report was created.</p></div></div></body></html>").ToString();
    }
}
