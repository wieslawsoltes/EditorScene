namespace EditorScene.App;

internal static class Samples
{
    internal const string CSharp = """
// EditorScene — native Uno Platform syntax editor
// Ctrl+Space: completion    Ctrl+K, Ctrl+D: format    F2: next bookmark
// The same document, language services and Skia renderer run on desktop and WebAssembly.

using System;
using System.Collections.Generic;
using System.Linq;

namespace EditorScene.Samples;

/// <summary>Tracks a collection of readings and publishes aggregate statistics.</summary>
public sealed class SensorMonitor
{
    private readonly List<Reading> _readings = new();

    public event EventHandler<Reading>? ReadingAdded;

    public int Count => _readings.Count;
    public double Average => _readings.Count == 0 ? 0 : _readings.Average(r => r.Value);

    /// <summary>Adds a timestamped reading after validating its value.</summary>
    public void Add(string source, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));

        var reading = new Reading(source, value, DateTimeOffset.UtcNow);
        _readings.Add(reading);
        ReadingAdded?.Invoke(this, reading);
        Console.WriteLine($"{source}: {value:F2}  |  Average: {Average:F2}");
    }

    public IEnumerable<Reading> Recent(TimeSpan interval)
    {
        var threshold = DateTimeOffset.UtcNow - interval;
        return _readings.Where(reading => reading.Timestamp >= threshold);
    }

    public void Clear() => _readings.Clear();
}

public sealed record Reading(string Source, double Value, DateTimeOffset Timestamp);

// Unicode shaping: مرحبا  שלום  日本語  🙂  é
""";
    internal const string VisualBasic = """
' EditorScene — Visual Basic semantic language services
Imports System
Imports System.Collections.Generic
Imports System.Linq

Namespace EditorScene.Samples
    Public Class SensorMonitor
        Private ReadOnly values As New List(Of Double)()

        Public ReadOnly Property Average As Double
            Get
                Return If(values.Count = 0, 0, values.Average())
            End Get
        End Property

        Public Sub Add(value As Double)
            If Not Double.IsFinite(value) Then
                Throw New ArgumentOutOfRangeException(NameOf(value))
            End If
            values.Add(value)
            Console.WriteLine("Average: {0:F2}", Average)
        End Sub
    End Class
End Namespace
""";
    internal const string Json = """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "name": "EditorScene",
  "platforms": ["Desktop", "WebAssembly"],
  "editor": {
    "fontSize": 13,
    "tabSize": 4,
    "lineNumbers": true,
    "wordWrap": false,
    "features": ["completion", "outlining", "diagnostics", "split views"]
  },
  "sensors": [
    { "name": "Temperature", "value": 21.8, "unit": "°C" },
    { "name": "Humidity", "value": 47.2, "unit": "%" }
  ],
  "unicode": "مرحبا 日本語 🙂 é"
}
""";
    internal const string Xml = """
<?xml version="1.0" encoding="utf-8"?>
<!-- EditorScene: XML navigation, highlighting, validation and formatting -->
<Workspace name="EditorScene" xmlns="urn:editorscene:samples">
  <Documents>
    <Document path="SensorMonitor.cs" language="C#" />
    <Document path="settings.json" language="JSON" />
  </Documents>
  <Preferences theme="Light">
    <Editor fontSize="13" tabSize="4">
      <LineNumbers>true</LineNumbers>
      <WordWrap>false</WordWrap>
      <Typography>مرحبا 日本語 🙂</Typography>
    </Editor>
  </Preferences>
</Workspace>
""";
    internal const string Xaml = """
<Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:editor="using:EditorScene.Controls"
      x:Class="Sample.EditorPage">
    <Grid RowSpacing="4">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <editor:NavigableSymbolSelector Editor="{Binding ElementName=Editor}" />
        <editor:SyntaxEditor x:Name="Editor" Grid.Row="1"
                             ShowLineNumbers="True" FontSize="13"
                             Document="{Binding Document}" />
    </Grid>
</Page>
""";
}
