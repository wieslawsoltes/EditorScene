using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace EditorScene.Languages.DotNet;

/// <summary>
/// Reference-assembly metadata embedded at build time. No Assembly.Location,
/// filesystem, network, execution of user code, or browser-specific heuristics.
/// </summary>
public static class ReferenceAssemblyCatalog
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(Load);
    public static ImmutableArray<MetadataReference> Default => References.Value;
    private static ImmutableArray<MetadataReference> Load()
    {
        var assembly = typeof(ReferenceAssemblyCatalog).Assembly;
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("EditorScene.Reference.", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Reference resource {name} could not be read.");
            using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            builder.Add(MetadataReference.CreateFromImage(ImmutableArray.Create(buffer.ToArray()), filePath: name[22..]));
        }
        if (builder.Count == 0) throw new InvalidOperationException("The language package contains no reference assemblies. Build with the .NET 10 targeting pack.");
        return builder.ToImmutable();
    }
}
