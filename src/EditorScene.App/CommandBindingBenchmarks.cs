using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EditorScene.Controls;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

internal static class CommandBindingBenchmarks
{
    internal static void Run(SyntaxEditor editor)
    {
        // The opt-in native test host supplies selected editable text and no macro session.
        using var bindings = new EditorCommandBindings { Editor = editor };
        var commands = new[] { EditorCommand.Copy, EditorCommand.Cut, EditorCommand.Paste };
        var controls = Enumerable.Range(0, 60).Select(i => (Target: new Button(), Command: commands[i % commands.Length])).ToArray();
        foreach (var (target, command) in controls) bindings.Bind(target, command, executeOnClick: false);
        var rows = new List<CommandBindingSample>();
        Measure("per_surface_advice", () =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
                foreach (var (target, command) in controls)
                {
                    var enabled = editor.GetCommandState(command, EditorCommandQueryMode.CommandSurface).IsEnabled;
                    if (target.IsEnabled != enabled) target.IsEnabled = enabled;
                }
        }, "6000 state queries: each of 60 native buttons independently queries its one of three commands for 100 refreshes. Same new advice API, not an exact earlier application implementation.");
        Measure("grouped_binding_advice", () =>
        {
            var queries = bindings.QueryCount; var writes = bindings.PropertyWriteCount;
            for (var iteration = 0; iteration < 100; iteration++) bindings.Refresh();
            if (bindings.QueryCount - queries != 300 || bindings.PropertyWriteCount != writes) throw new InvalidOperationException("Grouped query/work invariant failed.");
        }, "300 queries: 60 existing native bindings in three command groups, refreshed 100 times. Native property comparisons still visit every binding, but unchanged values are not written. Includes grouping dispatch overhead, excludes initial binding construction.");
        var report = new CommandBindingReport
        {
            sourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, rows = rows.ToArray()
        };
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/native-checks.binding-performance.json", JsonSerializer.Serialize(report, CommandBindingJsonContext.Default.CommandBindingReport));
        void Measure(string implementation, Action action, string note)
        {
            for (var i = 0; i < 3; i++) action();
            var elapsed = new double[11]; var allocated = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
                action(); elapsed[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds; allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(elapsed); Array.Sort(allocated);
            rows.Add(new() { implementation = implementation, p50Milliseconds = elapsed[5], p95Milliseconds = elapsed[10], medianAllocatedBytes = allocated[5], sortedElapsedMilliseconds = elapsed, sortedAllocatedBytes = allocated,
                note = note + " Three warmups, eleven samples, current-thread managed allocation only. Native controls are not attached for layout/painting. Document/editor/control construction excluded. No physical GPU, frame rate, input latency or Actipro comparison." });
            Console.WriteLine($"BINDING_BENCH {implementation} p50_ms={elapsed[5]:F4} p95_ms={elapsed[10]:F4} bytes={allocated[5]}");
        }
    }
}
internal sealed class CommandBindingReport
{
    public string sourceRevision { get; set; } = "";
    public string framework { get; set; } = "";
    public string os { get; set; } = "";
    public CommandBindingSample[] rows { get; set; } = [];
}
internal sealed class CommandBindingSample
{
    public string implementation { get; set; } = "";
    public double p50Milliseconds { get; set; }
    public double p95Milliseconds { get; set; }
    public long medianAllocatedBytes { get; set; }
    public double[] sortedElapsedMilliseconds { get; set; } = [];
    public long[] sortedAllocatedBytes { get; set; } = [];
    public string note { get; set; } = "";
}
[JsonSerializable(typeof(CommandBindingReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class CommandBindingJsonContext : JsonSerializerContext { }
