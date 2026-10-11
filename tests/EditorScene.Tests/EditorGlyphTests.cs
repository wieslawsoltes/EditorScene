using EditorScene.Rendering.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace EditorScene.Tests;

[TestFixture]
public sealed class EditorGlyphTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void EveryGlyphPaintsPixelsAndPreservesTheCallersCanvas(bool dark, bool highContrast)
    {
        using var bitmap = new SKBitmap(32, 32); using var canvas = new SKCanvas(bitmap); using var renderer = new EditorGlyphRenderer();
        foreach (var glyph in Enum.GetValues<EditorGlyph>())
        {
            canvas.Clear(SKColors.Transparent); canvas.Save(); canvas.Translate(1, 1);
            var matrix = canvas.TotalMatrix; var count = canvas.SaveCount;
            renderer.Draw(canvas, 24, 24, glyph, dark, highContrast);
            Assert.That(canvas.TotalMatrix, Is.EqualTo(matrix), glyph.ToString()); Assert.That(canvas.SaveCount, Is.EqualTo(count));
            Assert.That(bitmap.Pixels.Any(p => p.Alpha > 0), Is.True, glyph.ToString()); canvas.Restore();
        }
        Assert.That(renderer.BuildCount, Is.EqualTo(Enum.GetValues<EditorGlyph>().Length));
    }
    [Test]
    public void WarmReplayAtDifferentSizesDoesNotRebuildOrAllocateManagedObjects()
    {
        using var bitmap = new SKBitmap(32, 32); using var canvas = new SKCanvas(bitmap); using var renderer = new EditorGlyphRenderer();
        for (var i = 0; i < 20; i++) renderer.Draw(canvas, 16, 16, EditorGlyph.Save);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) renderer.Draw(canvas, i % 2 == 0 ? 16 : 24, 24, EditorGlyph.Save);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(renderer.BuildCount, Is.EqualTo(1)); Assert.That(allocated, Is.Zero);
    }
    [Test]
    public void KindAndPaletteChangesInvalidateExactlyOnceAndHighContrastIsDistinct()
    {
        using var bitmap = new SKBitmap(16, 16); using var canvas = new SKCanvas(bitmap); using var renderer = new EditorGlyphRenderer();
        renderer.Draw(canvas, 16, 16, EditorGlyph.Record); var light = bitmap.GetPixel(8, 8);
        renderer.Draw(canvas, 16, 16, EditorGlyph.Record, true); Assert.That(renderer.BuildCount, Is.EqualTo(2));
        renderer.Draw(canvas, 16, 16, EditorGlyph.Record, true, true); Assert.That(renderer.BuildCount, Is.EqualTo(3));
        Assert.That(bitmap.GetPixel(8, 8), Is.Not.EqualTo(light));
        renderer.Draw(canvas, 16, 16, EditorGlyph.Record, true, true); Assert.That(renderer.BuildCount, Is.EqualTo(3));
        renderer.Draw(canvas, 16, 16, EditorGlyph.Play, true, true); Assert.That(renderer.BuildCount, Is.EqualTo(4));
    }
    [Test]
    public void InvalidDimensionsKindsAndDisposedRendererAreExplicit()
    {
        using var bitmap = new SKBitmap(16, 16); using var canvas = new SKCanvas(bitmap); using var renderer = new EditorGlyphRenderer();
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Draw(canvas, double.NaN, 16, EditorGlyph.New));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Draw(canvas, 16, -1, EditorGlyph.New));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Draw(canvas, 16, 16, (EditorGlyph)int.MaxValue));
        renderer.Draw(canvas, 0, 16, EditorGlyph.New); Assert.That(renderer.BuildCount, Is.Zero);
        renderer.Dispose(); renderer.Dispose(); Assert.Throws<ObjectDisposedException>(() => renderer.Draw(canvas, 16, 16, EditorGlyph.New));
    }
}
