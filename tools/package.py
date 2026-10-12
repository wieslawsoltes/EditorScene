#!/usr/bin/env python3
"""Build the complete local NuGet feed and verify it outside the source tree."""
from __future__ import annotations
import hashlib, json, os, shutil, subprocess, tempfile, zipfile
from pathlib import Path
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts' / 'packages'
VERSION = '0.1.0-alpha.1'
PIN = '1a2df473f157a0127d2559f241dc712d1ce60188'
LIBRARIES = ['Text', 'LanguageServices', 'Languages.DotNet', 'Languages.Structured', 'Protocol', 'Rendering.Skia', 'Snippets', 'Controls']
DEPENDENCIES = ['UnoEdit.Core', 'UnoEdit.Rendering.Skia', 'UnoEdit.Uno']

def run(*args, cwd=ROOT, env=None):
    print('+ ' + ' '.join(map(str, args)), flush=True)
    subprocess.run(list(map(str,args)), cwd=cwd, env=env, check=True)

def main():
    actual = subprocess.check_output(['git','-C',str(ROOT/'external/UnoEdit'),'rev-parse','HEAD'],text=True).strip()
    if actual != PIN: raise RuntimeError('The editor dependency is not pinned to the validated source.')
    shutil.rmtree(OUT, ignore_errors=True); OUT.mkdir(parents=True)
    projects = [ROOT/'external/UnoEdit/src'/name/(name+'.csproj') for name in DEPENDENCIES]
    projects += [ROOT/'src'/('EditorScene.'+name)/('EditorScene.'+name+'.csproj') for name in LIBRARIES]
    for project in projects:
        run('dotnet','pack',project,'-c','Release','-o',OUT,'-p:ContinuousIntegrationBuild=true')
    packages = {}
    for package in OUT.glob('*.nupkg'):
        with zipfile.ZipFile(package) as archive:
            spec = [name for name in archive.namelist() if name.endswith('.nuspec')]
            if len(spec)!=1: raise RuntimeError(f'Invalid package metadata: {package}')
            meta=ET.fromstring(archive.read(spec[0])).find('{*}metadata')
            identity=meta.find('{*}id').text; version=meta.find('{*}version').text
            if version != VERSION: raise RuntimeError(f'Unexpected package version: {identity} {version}')
            if not any(name.endswith('.dll') and name.startswith('lib/') for name in archive.namelist()): raise RuntimeError(f'No runtime library: {identity}')
            packages[identity]={'version':version,'file':package.name,'sha256':hashlib.sha256(package.read_bytes()).hexdigest()}
    expected=set(DEPENDENCIES + ['EditorScene.'+name for name in LIBRARIES])
    if set(packages)!=expected: raise RuntimeError(f'Unexpected feed contents: {set(packages)^expected}')
    with tempfile.TemporaryDirectory(prefix='editorscene-consumer-') as temporary:
        directory=Path(temporary); cache=directory/'nuget'; env=dict(os.environ,NUGET_PACKAGES=str(cache))
        shutil.copyfile(ROOT/'global.json',directory/'global.json')
        # Prefix-specific mapping and a clean global cache ensure the consumer uses
        # these exact packages, never a public package with the same ID/version.
        (directory/'NuGet.Config').write_text(f'''<configuration><packageSources><clear/>
          <add key="EditorScene" value="{escape(str(OUT))}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>
          </packageSources><packageSourceMapping>
          <packageSource key="EditorScene"><package pattern="EditorScene.*"/><package pattern="UnoEdit.*"/></packageSource>
          <packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>''')
        console=directory/'Headless'; console.mkdir()
        references=''.join(f'<PackageReference Include="EditorScene.{name}" Version="{VERSION}"/>' for name in LIBRARIES if name!='Controls')
        (console/'Headless.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup>{references}</ItemGroup></Project>')
        (console/'Program.cs').write_text('''using EditorScene.Text;
using EditorScene.Languages.DotNet;
using EditorScene.Languages.Structured;
using EditorScene.Snippets;
using EditorScene.Rendering.Skia;
using EditorScene.LanguageServices;
using System.Reflection;
using var document = new EditorDocument("using System; class C { int M() => Math.Abs(-1); }", "consumer.cs");
var snapshot = document.Snapshot();
using var whitespaceDocument = new EditorDocument("a" + (char)9 + "b"); var whitespaceSnapshot = whitespaceDocument.Snapshot();
var whitespacePlan = WhitespaceEditPlan.Create(whitespaceSnapshot.GetSpan(0, whitespaceSnapshot.Length), WhitespaceConversion.Spaces);
whitespacePlan.Apply(whitespaceDocument);
if (whitespaceDocument.Buffer.Text != "a   b" || whitespacePlan.Edits.Length != 1) throw new Exception("Packaged whitespace planning failed.");
using var tabsDocument = new EditorDocument("\tX");
var tabs = new TabColumnMap(tabsDocument.Snapshot(), 0, 8);
if (tabs.GetColumn(2) != 9 || tabs.GetOffset(7) != 0 || tabs.GetIndentation(2, true).Length != 7) throw new Exception("Packaged tab mapping failed.");
var multi = new MultiSelection(tabsDocument); multi.Set([new SelectionRange(0, 0), new SelectionRange(2, 2)]);
multi.ReplaceEach(["a", "bc"]);
if (tabsDocument.Buffer.Text != "a\tXbc") throw new Exception("Packaged per-caret replacement failed.");
using var softDocument = new EditorDocument("      x");
var softMap = new TabColumnMap(softDocument.Snapshot(), 0, 4);
if (softMap.LeadingWhitespaceLength != 6 || softMap.GetSoftTabBackspaceLength(6) != 2) throw new Exception("Packaged soft-tab mapping failed.");
var softSelections = new MultiSelection(softDocument); softSelections.Set([new SelectionRange(2, 2), new SelectionRange(6, 6)]);
softSelections.DeleteToPreviousTabStop(4);
if (softDocument.Buffer.Text != "  x") throw new Exception("Packaged atomic soft-tab Backspace failed.");
softDocument.Buffer.UndoStack.Undo();
if (softDocument.Buffer.Text != "      x") throw new Exception("Packaged soft-tab Undo failed.");
if (snapshot.GetLine(0).Length != snapshot.Length) throw new Exception("Packaged snapshot line index failed.");
document.Buffer.Insert(0, " "); var changed = document.Snapshot();
var minimal = SnapshotTextChanges.GetMinimalChange(snapshot, changed);
if (minimal.Offset != 0 || minimal.Length != 0 || minimal.NewText != " ") throw new Exception("Packaged history change failed.");
var semantic = EditorScene.Protocol.SemanticTokenData.FromFull(System.Text.Json.Nodes.JsonNode.Parse("{\\"resultId\\":\\"a\\",\\"data\\":[0,0,5,0,0]}"));
var delta = semantic.ApplyDelta(System.Text.Json.Nodes.JsonNode.Parse("{\\"resultId\\":\\"b\\",\\"edits\\":[{\\"start\\":1,\\"deleteCount\\":1,\\"data\\":[1]}]}"));
if (delta.Data[1] != 1 || semantic.Data[1] != 0) throw new Exception("Packaged immutable semantic delta failed.");
var literal = new SnapshotLiteralSearch("class", wholeWord: true);
if (literal.FindNext(snapshot, 0)?.Text != "class") throw new Exception("Packaged literal search failed.");
var map = new EditorScene.LanguageServices.BracketMap(snapshot, []);
if (map.PairCount < 1) throw new Exception("Packaged bracket map failed.");
var rangeIndex = new EditorScene.LanguageServices.RangeIndex<int>([0, 5, 10], x => new(x, 1));
var hits = new List<int>(); rangeIndex.CopyOverlaps(new(4, 3), hits);
if (hits.Count != 1 || hits[0] != 5) throw new Exception("Packaged range index failed.");
var selectionRanges = await new DotNetLanguage().GetSelectionRangesAsync(snapshot, 0);
var expansion = new EditorScene.LanguageServices.SelectionExpansion(snapshot, new SelectionRange(0, 0), selectionRanges);
if (!expansion.TryExpand(snapshot, expansion.Current, out var selected) || selected.Length == 0) throw new Exception("Packaged structural selection failed.");
if (!expansion.TryContract(snapshot, selected, out var original) || original != new SelectionRange(0, 0)) throw new Exception("Packaged structural contraction failed.");
var analysis = await new DotNetLanguage().AnalyzeAsync(snapshot);
var diagnosticTable = new EditorScene.LanguageServices.DiagnosticTable(snapshot, analysis);
if (diagnosticTable.Filter() != diagnosticTable.Rows || !diagnosticTable.IsCurrent(snapshot)) throw new Exception("Packaged diagnostic table failed.");
var queryTable = new DiagnosticTable(snapshot, analysis with { Diagnostics = [new("B", "second", IssueSeverity.Warning, new(1, 1)), new("A", "first", IssueSeverity.Error, new(0, 1))] });
var queryView = new DiagnosticTableView(queryTable);
var sorted = queryView.Query(column: DiagnosticSortColumn.Code);
var cursor = new DiagnosticNavigationCursor(sorted);
if (!cursor.TryMove(out var firstDiagnostic) || firstDiagnostic.Code != "A" || !cursor.TryMove(out var secondDiagnostic) || secondDiagnostic.Code != "B") throw new Exception("Packaged diagnostic cursor failed.");
if (cursor.TryMove(out _, wrap: false) || !cursor.TryMove(out firstDiagnostic) || firstDiagnostic.Code != "A") throw new Exception("Packaged diagnostic cursor boundaries failed.");
if (sorted[0].Code != "A" || sorted[1].Code != "B" || queryView.Query(column: DiagnosticSortColumn.Code) != sorted || queryView.SortBuildCount != 1) throw new Exception("Packaged diagnostic ordering/cache failed.");
if (queryView.Query(DiagnosticVisibility.Warning, "second", DiagnosticSortColumn.Code).Single().Code != "B" || queryView.SortBuildCount != 1) throw new Exception("Packaged ordered filtering failed.");
using var glyphBitmap = new SkiaSharp.SKBitmap(16, 16); using var glyphCanvas = new SkiaSharp.SKCanvas(glyphBitmap);
using var glyphRenderer = new EditorGlyphRenderer();
glyphRenderer.Draw(glyphCanvas, 16, 16, EditorGlyph.Save); glyphRenderer.Draw(glyphCanvas, 16, 16, EditorGlyph.Save);
if (glyphRenderer.BuildCount != 1) throw new Exception("Packaged glyph cache failed.");
if (analysis.Diagnostics.Any(d => d.Severity == EditorScene.LanguageServices.IssueSeverity.Error)) throw new Exception("Packaged compiler references failed.");
using var jsonDocument = new EditorDocument("{\\"x\\":42}");
var jsonSnapshot = jsonDocument.Snapshot();
if (!(await new JsonLanguage().AnalyzeAsync(jsonSnapshot)).Diagnostics.IsEmpty) throw new Exception("Packaged JSON provider failed.");
var catalog = SnippetCatalog.CreateDefault(); using var stream = new MemoryStream();
CodeSnippetSerializer.Save(stream, catalog.Entries.Select(e => e.Snippet)); stream.Position = 0;
if (CodeSnippetSerializer.Load(stream).Length != 16) throw new Exception("Packaged snippets failed.");
if (EditorTheme.Light.ColorFor("keyword") == EditorTheme.Light.Foreground) throw new Exception("Packaged rendering palette failed.");
if (Assembly.Load("EditorScene.Protocol").GetExportedTypes().Length == 0) throw new Exception("Protocol package is empty.");
Console.WriteLine("Package-only document, compiler, JSON, snippet, rendering, diagnostic query, soft-tab and protocol consumer passed.");
''')
        run('dotnet','run','--project',console/'Headless.csproj','-c','Release',cwd=directory,env=env)
        native=directory/'Native'; native.mkdir()
        (native/'Native.csproj').write_text(f'''<Project Sdk="Uno.Sdk"><PropertyGroup>
          <TargetFrameworks>net10.0-desktop;net10.0-browserwasm</TargetFrameworks><UnoFeatures>SkiaRenderer</UnoFeatures>
          <OutputType>Library</OutputType><IsPackable>false</IsPackable><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
          </PropertyGroup><ItemGroup><PackageReference Include="EditorScene.Controls" Version="{VERSION}"/>
          <PackageReference Include="EditorScene.Languages.Structured" Version="{VERSION}"/></ItemGroup></Project>''')
        (native/'ConsumerControl.cs').write_text('''using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Languages.Structured;
using EditorScene.Snippets;
using EditorScene.Text;
using Microsoft.UI.Xaml.Controls;
public sealed class ConsumerControl : UserControl
{
    public EditorCommandBindings Commands { get; } = new();
    public SyntaxEditor Editor { get; } = new() { Document = new EditorDocument("{}", "consumer.json"), SyntaxLanguage = new JsonLanguage() };
    public Task<bool> ExpandAsync() => Editor.ExpandSelectionAsync();
    public bool Contract() => Editor.ContractSelection();
    public bool ConvertWhitespace() => Editor.ConvertSelectedWhitespace(false);
    public bool TabifyLines() => Editor.TabifySelectedLines(true);
    public EditorCommandState QueryCopy() => Editor.GetCommandState(EditorCommand.Copy);
    public bool ShowContext() => Editor.ShowEditorContextMenu();
    public ConsumerControl()
    {
        Editor.SetDocumentAndLanguage(Editor.Document, new JsonLanguage());
        Editor.IsDefaultContextMenuEnabled = true; Editor.CanMoveCaretForSecondaryPointerButton = false;
        Editor.EditorContextMenu.Requested += (_, args) => { if (Editor.IsReadOnly) args.Cancel = true; };
        Editor.NavigateToMatchingBracket();
        var options = new EditorViewOptions { TabSize = 8, ConvertTabsToSpaces = true, CanBackspaceOverSpacesToTabStop = true };
        Editor.ApplyViewOptions(options);
        _ = Editor.GetCaretTabColumn();
        Editor.CanBackspaceOverSpacesToTabStop = true;
        Editor.Backspace();
        var optionsPanel = new EditorOptionsView(options);
        _ = optionsPanel.SoftTabBackspaceInput;
        _ = optionsPanel.TryGetOptions(out var captured);
        optionsPanel.Dispose();
        var navigation = new NavigableSymbolSelector { Editor = Editor };
        var outline = new DocumentOutline { Editor = Editor };
        var picker = new SnippetPicker(Editor.Snippets, "json", SnippetTypes.Expansion);
        Editor.CopyViewSettingsFrom(Editor);
        Commands.Editor = Editor;
        var copyButton = new Button { Content = "Copy" };
        Commands.Bind(copyButton, EditorCommand.Copy);
        Commands.Bind(new MenuFlyoutItem(), EditorCommand.Copy, executeOnClick: false);
        Commands.Refresh();
        if (Commands.Count != 2 || Commands.DistinctCommandCount != 1) throw new Exception("Packaged command binding contract failed.");
        _ = Editor.GetCommandState(EditorCommand.Indent, EditorCommandQueryMode.CommandSurface);
        var diagnostics = new DiagnosticList { Editor = Editor };
        diagnostics.SortBy(DiagnosticSortColumn.Line, DiagnosticSortDirection.Descending);
        diagnostics.SetColumnWidth(DiagnosticSortColumn.File, 180);
        _ = diagnostics.NavigateNextDiagnostic(backwards: true, wrap: false);
        _ = diagnostics.PreviousDiagnosticButton;
        _ = diagnostics.NextDiagnosticButton;
        _ = diagnostics.GetSortButton(DiagnosticSortColumn.Code);
        _ = diagnostics.GetColumnGrip(DiagnosticSortColumn.File);
        Resources.MergedDictionaries.Add(new DiagnosticTemplates());
        var status = new EditorStatusBar { Editor = Editor };
        var split = new ResizableSplitView { First = Editor, Ratio = .4 };
        Resources.MergedDictionaries.Add(EditorChrome.Create(EditorScene.Rendering.Skia.EditorTheme.Light));
        Content = new StackPanel { Children = { copyButton, navigation, split, status, diagnostics, outline, picker, new EditorIcon() } };
    }
}
''')
        run('dotnet','build',native/'Native.csproj','-c','Release',cwd=directory,env=env)
        asset_files=[console/'obj/project.assets.json',native/'obj/project.assets.json']
        restored=set()
        for file in asset_files:
            data=json.loads(file.read_text())
            if any(entry.get('type')=='project' for entry in data['libraries'].values()): raise RuntimeError('A consumer used a source ProjectReference.')
            restored.update(name.split('/')[0] for name in data['libraries'])
        if not expected <= restored: raise RuntimeError(f'Packages not consumed: {expected-restored}')
        for identity, item in packages.items():
            payload=cache/identity.lower()/VERSION/(identity.lower()+'.'+VERSION+'.nupkg')
            if not payload.is_file() or hashlib.sha256(payload.read_bytes()).hexdigest()!=item['sha256']:
                raise RuntimeError(f'Consumer did not restore the exact built package: {identity}')
        report={'passed':True,'sourceRevision':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
                'dependencyRevision':PIN,'packages':packages,'consumers':['net10.0 executable (executed)','net10.0-desktop control library (compiled)','net10.0-browserwasm control library (compiled)'],'usesProjectReferences':False}
        (OUT/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
        shutil.copyfile(console/'Headless.csproj',OUT/'headless-consumer.csproj.example')
        shutil.copyfile(console/'Program.cs',OUT/'headless-consumer.cs.example')
        shutil.copyfile(native/'Native.csproj',OUT/'native-consumer.csproj.example')
        shutil.copyfile(native/'ConsumerControl.cs',OUT/'native-consumer.cs.example')
    (OUT/'SHA256SUMS.txt').write_text(''.join(f"{item['sha256']}  {item['file']}\n" for _,item in sorted(packages.items())))
    print(json.dumps(report,indent=2),flush=True)
if __name__=='__main__': main()
