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
CONSUMERS = ROOT / 'tools' / 'package-consumers'

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
        # Copy actual C# verbatim: Python string escapes must not turn a C# \n
        # delimiter into a physical newline inside a quoted C# constant.
        shutil.copyfile(CONSUMERS/'Headless.cs',console/'Program.cs')
        run('dotnet','run','--project',console/'Headless.csproj','-c','Release',cwd=directory,env=env)
        native=directory/'Native'; native.mkdir()
        (native/'Native.csproj').write_text(f'''<Project Sdk="Uno.Sdk"><PropertyGroup>
          <TargetFrameworks>net10.0-desktop;net10.0-browserwasm</TargetFrameworks><UnoFeatures>SkiaRenderer</UnoFeatures>
          <OutputType>Library</OutputType><IsPackable>false</IsPackable><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
          </PropertyGroup><ItemGroup><PackageReference Include="EditorScene.Controls" Version="{VERSION}"/>
          <PackageReference Include="EditorScene.Languages.Structured" Version="{VERSION}"/></ItemGroup></Project>''')
        shutil.copyfile(CONSUMERS/'Native.cs',native/'ConsumerControl.cs')
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
