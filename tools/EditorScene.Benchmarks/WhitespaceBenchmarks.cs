using System.Diagnostics;
using EditorScene.Text;

internal static class WhitespaceBenchmarks
{
    internal static void Add(List<object> rows)
    {
        var text = string.Concat(Enumerable.Repeat("    " + new string('x', 100_000) + "  \n", 40));
        using var document = new EditorDocument(text); var snapshot = document.Snapshot();
        var scope = snapshot.GetSpan(0, snapshot.Length); _ = snapshot.GetLine(0);
        TextEdit[] Previous() => document.Buffer.Lines.Select(line => new TextEdit(line.Offset, line.Length,
            PreviousTransform(document.Buffer.GetText(line.Offset, line.Length))))
            .Where(edit => document.Buffer.GetText(edit.Offset, edit.Length) != edit.NewText).ToArray();
        WhitespaceEditPlan Current() => WhitespaceEditPlan.Create(scope, WhitespaceConversion.Tabs, leadingOnly: true);
        // Prove equivalent final bytes outside the timed region. The baseline
        // reproduces ConvertIndentation/EditSelectedLines from d80936a.
        using (var previous = new EditorDocument(text))
        using (var current = new EditorDocument(text))
        {
            previous.ApplyEdits(Previous(), previous.Revision);
            current.ApplyEdits(Current().Edits, current.Revision);
            if (previous.Buffer.Text != current.Buffer.Text) throw new InvalidOperationException("Whitespace benchmark paths produced different text.");
        }
        Measure("indent_conversion_40_long_lines", "previous_full_line_edits", () =>
        {
            var edits = Previous(); if (edits.Length != 40) throw new InvalidOperationException("Invalid baseline fixture");
        }, "Reproduces preceding ConvertIndentation plus EditSelectedLines: reads, transforms and compares whole lines. Builds forty full-line edits over about four million UTF-16 units.");
        Measure("indent_conversion_40_long_lines", "current_whitespace_run_edits", () =>
        {
            var plan = Current(); if (plan.Edits.Length != 40 || plan.ReplacementCharacters != 40) throw new InvalidOperationException("Invalid streaming fixture");
        }, "Same input and final bytes, but forty four-space-to-one-tab edits; no code content is materialized or replaced. Includes scanning and plan construction, not application/undo/UI. Shared reader pool and line index are warm.");
        using var noChange = new EditorDocument(new string('x', 4_000_000)); var unchanged = noChange.Snapshot(); var entire = unchanged.GetSpan(0, unchanged.Length);
        _ = unchanged.GetLine(0);
        Measure("whitespace_scan_4000000_unchanged", "current", () =>
        {
            var plan = WhitespaceEditPlan.Create(entire, WhitespaceConversion.Spaces);
            if (!plan.Edits.IsEmpty) throw new InvalidOperationException("An unchanged source produced edits");
        }, "Scans a four-million-unit line without whitespace. Records no replacements and never creates a full source string. No previous-path comparison for this separate fixture.");

        void Measure(string scenario, string implementation, Action action, string note)
        {
            for (var i = 0; i < 3; i++) action();
            var times = new double[11]; var allocations = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                action(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            Array.Sort(times); Array.Sort(allocations);
            rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = allocations[5],
                sortedElapsedMilliseconds = times, sortedAllocatedBytes = allocations,
                note = note + " Three warmups, eleven samples, setup and equivalence check excluded. Sequential baseline/current groups; current-thread managed allocations only. No physical GPU, frame-rate, full editor-latency or Actipro performance claim." });
            Console.WriteLine($"WHITESPACE_BENCH {scenario} {implementation} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={allocations[5]}");
        }
    }
    private static string PreviousTransform(string text)
    {
        const int tabSize = 4; var end = 0; var columns = 0;
        while (end < text.Length && text[end] is ' ' or '\t') { columns += text[end] == '\t' ? tabSize - columns % tabSize : 1; end++; }
        return new string('\t', columns / tabSize) + new string(' ', columns % tabSize) + text[end..];
    }
}
