using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Rendering.Skia;

namespace EditorScene.Tests;

[TestFixture]
public sealed class AdornmentReviewTests
{
    [Test]
    public void DeepViewportPaintPrunesDiagnosticsAndClipsLongSpans()
    {
        using var document = new EditorDocument(string.Join('\n', Enumerable.Repeat("text", 100_000)));
        using var viewport = new DocumentViewport(document.Buffer) { Style = new() { FontFamily = "DejaVu Sans Mono", FontSize = 13 } };
        using var adornments = new EditorAdornments(document, viewport, document.Indicators);
        var diagnostics = Enumerable.Range(0, 100_000).Select(i => new EditorDiagnostic("TEST", "item", IssueSeverity.Error, new(i * 5, 1)))
            .Prepend(new("LONG", "long span", IssueSeverity.Warning, new(0, document.Buffer.TextLength))).ToImmutableArray();
        adornments.Update(new(document.Id, document.Revision, diagnostics, [], [], []));
        viewport.SetViewport(640, 240); viewport.ScrollToLine(99_990);
        using var surface = SKSurface.Create(new SKImageInfo(640, 240)); viewport.Render(surface.Canvas);
        Assert.That(adornments.LastDiagnosticQuery.Matches, Is.InRange(2, 30));
        Assert.That(adornments.LastDiagnosticQuery.VisitedNodes, Is.LessThan(100));
        Assert.That(viewport.CachedLineCount, Is.LessThanOrEqualTo(DocumentViewport.CacheCapacity));
        var created = viewport.LayoutCreationCount; viewport.Render(surface.Canvas);
        Assert.That(viewport.LayoutCreationCount, Is.EqualTo(created));
    }
    [Test]
    public void StaleAndInvalidProviderResultsCannotReplaceValidIndex()
    {
        using var document = new EditorDocument("abc"); using var viewport = new DocumentViewport(document.Buffer);
        using var adornments = new EditorAdornments(document, viewport, document.Indicators);
        adornments.Update(new(document.Id, 0, [new("ok", "", IssueSeverity.Error, new(0, 1))], [], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => adornments.Update(new(document.Id, 0, [new("bad", "", IssueSeverity.Error, new(-1, 1))], [], [], [])));
        viewport.SetViewport(320, 100); using var surface = SKSurface.Create(new SKImageInfo(320, 100)); viewport.Render(surface.Canvas);
        Assert.That(adornments.LastDiagnosticQuery.Matches, Is.EqualTo(1));
        document.Buffer.Insert(0, "X"); viewport.Render(surface.Canvas); Assert.That(adornments.LastDiagnosticQuery.Matches, Is.Zero);
    }
    [Test]
    public void IndicatorOrderingTracksEditsAndCollectionMutations()
    {
        using var document = new EditorDocument("ab\ncd\nef"); var indicators = document.Indicators;
        var second = indicators.Add(6, 0, IndicatorKind.Bookmark); var first = indicators.Add(0, 0, IndicatorKind.Bookmark);
        Assert.That(indicators.Items, Is.SameAs(indicators.Items)); Assert.That(indicators.GetInRange(0, 0).Single(), Is.SameAs(first));
        document.Buffer.Insert(0, "prefix\n"); Assert.That(indicators.GetInRange(13, 0).Single(), Is.SameAs(second));
        indicators.Remove(first); Assert.That(indicators.Next(0, IndicatorKind.Bookmark), Is.SameAs(second));
        indicators.Clear(); Assert.That(indicators.GetInRange(0, document.Buffer.TextLength), Is.Empty);
    }
}
