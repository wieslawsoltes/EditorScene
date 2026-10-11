using System.Net;
using System.Text;

namespace EditorScene.Text;

public readonly record struct ExportStyle(int Offset, int Length, string Color, bool Bold = false, bool Italic = false);

public static class TextExport
{
    /// <summary>Stand-alone UTF-8 HTML; user content is always HTML-encoded.</summary>
    public static string ToHtml(DocumentSnapshot snapshot, IEnumerable<ExportStyle>? styles = null, bool lineNumbers = false)
    {
        var output = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>");
        output.Append(WebUtility.HtmlEncode(snapshot.FileName));
        output.Append("</title><style>body{margin:24px}pre{font:14px/1.5 ui-monospace,monospace;tab-size:4;white-space:pre-wrap}.line{color:#687080;user-select:none;margin-right:20px}</style></head><body><pre>");
        var text = snapshot.Text; var position = 0;
        foreach (var span in ValidateStyles(styles ?? [], text.Length))
        {
            output.Append(WebUtility.HtmlEncode(text[position..span.Offset]));
            output.Append("<span style=\"color:").Append(span.Color);
            if (span.Bold) output.Append(";font-weight:bold");
            if (span.Italic) output.Append(";font-style:italic");
            output.Append("\">").Append(WebUtility.HtmlEncode(text.Substring(span.Offset, span.Length))).Append("</span>");
            position = span.Offset + span.Length;
        }
        output.Append(WebUtility.HtmlEncode(text[position..]));
        var html = output.ToString();
        if (lineNumbers)
        {
            var start = html.IndexOf("<pre>", StringComparison.Ordinal) + 5;
            var content = html[start..].Replace("\r\n", "\n").Replace('\r', '\n');
            var index = 0;
            html = html[..start] + string.Join("\n", content.Split('\n').Select(line => $"<span class=\"line\">{++index,4}</span>{line}"));
        }
        return html + "</pre></body></html>";
    }

    /// <summary>Unicode-safe RTF with an explicit color table and escaped control characters.</summary>
    public static string ToRtf(DocumentSnapshot snapshot, IEnumerable<ExportStyle>? styles = null)
    {
        var spans = ValidateStyles(styles ?? [], snapshot.Length).ToArray();
        var colors = spans.Select(s => s.Color).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var output = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern Consolas;}}{\colortbl ;");
        foreach (var color in colors)
        {
            output.Append(@"\red").Append(Convert.ToInt32(color.Substring(1, 2), 16));
            output.Append(@"\green").Append(Convert.ToInt32(color.Substring(3, 2), 16));
            output.Append(@"\blue").Append(Convert.ToInt32(color.Substring(5, 2), 16)).Append(';');
        }
        output.Append(@"}\f0\fs20\uc1 ");
        var spanIndex = 0; var activeEnd = -1;
        for (var i = 0; i < snapshot.Text.Length; i++)
        {
            if (activeEnd >= 0 && i >= activeEnd) { output.Append(@"\cf0\b0\i0 "); activeEnd = -1; }
            if (spanIndex < spans.Length && spans[spanIndex].Offset == i)
            {
                var span = spans[spanIndex++]; activeEnd = span.Offset + span.Length;
                output.Append(@"\cf").Append(Array.FindIndex(colors, c => c.Equals(span.Color, StringComparison.OrdinalIgnoreCase)) + 1);
                output.Append(span.Bold ? @"\b" : @"\b0").Append(span.Italic ? @"\i " : @"\i0 ");
            }
            var c = snapshot.Text[i];
            switch (c)
            {
                case '\\': case '{': case '}': output.Append('\\').Append(c); break;
                case '\r': if (i + 1 < snapshot.Length && snapshot.Text[i + 1] == '\n') i++; output.Append(@"\par "); break;
                case '\n': output.Append(@"\par "); break;
                case '\t': output.Append(@"\tab "); break;
                default: if (c > 127) output.Append(@"\u").Append(unchecked((short)c)).Append('?'); else output.Append(c); break;
            }
        }
        return output.Append('}').ToString();
    }
    private static IEnumerable<ExportStyle> ValidateStyles(IEnumerable<ExportStyle> styles, int length)
    {
        var end = 0;
        foreach (var span in styles.OrderBy(s => s.Offset))
        {
            if (span.Offset < end || span.Length <= 0 || span.Offset > length || span.Length > length - span.Offset)
                throw new ArgumentException("Export styles must be positive-length, disjoint and inside the document.", nameof(styles));
            if (span.Color.Length != 7 || span.Color[0] != '#' || span.Color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
                throw new ArgumentException("Colors must use #RRGGBB notation.", nameof(styles));
            end = span.Offset + span.Length; yield return span;
        }
    }
}
