using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EditorScene.Controls;

namespace EditorScene.App;

internal static class NativeCommandBenchmarks
{
    // Invoked only by the opt-in native regression host, never normal startup.
    internal static void Run(SyntaxEditor editor)
    {
        const int iterations = 10_000;
        var samples = new double[11]; var allocated = new long[11];
        for (var i = 0; i < 3; i++) Query();
        for (var i = 0; i < samples.Length; i++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            Query(); samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; allocated[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
        }
        Array.Sort(samples); Array.Sort(allocated);
        var report = new CommandBenchmarkReport
        {
            sourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            scenario = "native_copy_command_queries_10000", samples = samples.Length, warmups = 3,
            p50Milliseconds = samples[5], p95Milliseconds = samples[10], medianAllocatedBytes = allocated[5],
            sortedElapsedMilliseconds = samples, sortedAllocatedBytes = allocated,
            note = "10000 enabled Copy command-state queries on one actual native editor with selected text. Existing language/view/selection setup excluded. Current-only workload, not a prior-version or Actipro comparison. No menu construction/layout, clipboard reads, protection callbacks, painting, physical-GPU or end-to-end input-latency measurement. Current-thread managed allocations only."
        };
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/native-checks.command-performance.json", JsonSerializer.Serialize(report, CommandBenchmarkJsonContext.Default.CommandBenchmarkReport));
        Console.WriteLine($"COMMAND_BENCH native_copy_command_queries_10000 p50_ms={samples[5]:F4} p95_ms={samples[10]:F4} bytes={allocated[5]}");
        void Query()
        {
            var count = 0;
            for (var i = 0; i < iterations; i++) if (editor.GetCommandState(EditorCommand.Copy).IsEnabled) count++;
            if (count != iterations) throw new InvalidOperationException("Command query benchmark fixture changed.");
        }
    }
}

internal sealed class CommandBenchmarkReport
{
    public string sourceRevision { get; set; } = "";
    public string framework { get; set; } = "";
    public string os { get; set; } = "";
    public string scenario { get; set; } = "";
    public int samples { get; set; }
    public int warmups { get; set; }
    public double p50Milliseconds { get; set; }
    public double p95Milliseconds { get; set; }
    public long medianAllocatedBytes { get; set; }
    public double[] sortedElapsedMilliseconds { get; set; } = [];
    public long[] sortedAllocatedBytes { get; set; } = [];
    public string note { get; set; } = "";
}
[JsonSerializable(typeof(CommandBenchmarkReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class CommandBenchmarkJsonContext : JsonSerializerContext { }
