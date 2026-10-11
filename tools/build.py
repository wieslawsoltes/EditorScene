#!/usr/bin/env python3
"""Build and validate native EditorScene artifacts without substituting a UI framework."""
from __future__ import annotations
import argparse, hashlib, json, os, re, shutil, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts'
APP = ROOT / 'src/EditorScene.App/EditorScene.App.csproj'
PIN = '1a2df473f157a0127d2559f241dc712d1ce60188'

def run(*args: str, **kwargs):
    print('+ ' + ' '.join(map(str, args)), flush=True)
    return subprocess.run(list(map(str, args)), cwd=ROOT, check=True, **kwargs)

def verify_source():
    actual = subprocess.check_output(['git', '-C', str(ROOT / 'external/UnoEdit'), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != PIN:
        raise RuntimeError(f'Unexpected editor dependency: {actual}; expected {PIN}')

def tests(configuration):
    projects = {'editorscene': ROOT/'tests/EditorScene.Tests/EditorScene.Tests.csproj',
                'unoedit-core': ROOT/'external/UnoEdit/test/UnoEdit.Core.Tests/UnoEdit.Core.Tests.csproj',
                'unoedit-rendering': ROOT/'external/UnoEdit/test/UnoEdit.Rendering.Skia.Tests/UnoEdit.Rendering.Skia.Tests.csproj'}
    results = {}
    for name, project in projects.items():
        dest = OUT/'tests'/name; dest.mkdir(parents=True, exist_ok=True)
        trx = dest/(name+'.trx'); trx.unlink(missing_ok=True)
        run('dotnet', 'test', project, '-c', configuration, '--logger', f'trx;LogFileName={trx.name}', '--results-directory', dest)
        counters = ET.parse(trx).find('.//{*}Counters')
        if counters is None: raise RuntimeError(f'No test result counters: {trx}')
        result = {key:int(counters.get(key,'0')) for key in ('total','executed','passed','failed','error','timeout','aborted','notExecuted')}
        if result['executed'] <= 0 or any(result[k] for k in ('failed','error','timeout','aborted')):
            raise RuntimeError(f'Test failure: {name}: {result}')
        results[name] = result
    (OUT/'tests/summary.json').write_text(json.dumps(results, indent=2)+'\n')
    run('dotnet', 'run', '--project', ROOT/'tools/EditorScene.Benchmarks', '-c', configuration, '--', OUT/'tests/performance.json')

def desktop(configuration):
    run('dotnet', 'build', APP, '-c', configuration, '-f', 'net10.0-desktop',
        '-p:EditorSceneTargetFrameworks=net10.0-desktop', '-p:UnoEditTargetFrameworks=net10.0-desktop', f'-bl:{OUT}/desktop.binlog')

def native_checks(configuration):
    files = [p for p in (APP.parent/'bin').rglob('EditorScene.App.dll') if configuration in p.parts and p.parent.name == 'net10.0-desktop']
    if len(files) != 1: raise RuntimeError(f'Expected one desktop entry assembly; found {files}')
    env = dict(os.environ, EDITORSCENE_NATIVE_CHECKS='1')
    with (OUT/'native-checks.log').open('w') as log:
        run('xvfb-run', '-a', 'dotnet', files[0], env=env, stdout=log, stderr=subprocess.STDOUT, timeout=120)
    text = (OUT/'native-checks.log').read_text()
    checks = [line[6:] for line in text.splitlines() if line.startswith('PASS: ')]
    if len(checks) < 104 or 'EditorScene native checks passed.' not in text: raise RuntimeError('Native check evidence is incomplete.')
    (OUT/'native-checks.json').write_text(json.dumps({'passed': True, 'count': len(checks), 'checks': checks}, indent=2)+'\n')
    print(f'{len(checks)} actual Uno-host checks passed.', flush=True)

def browser(configuration, base):
    destination = OUT/'browser'; shutil.rmtree(destination, ignore_errors=True)
    run('dotnet', 'publish', APP, '-c', configuration, '-f', 'net10.0-browserwasm',
        '-p:EditorSceneTargetFrameworks=net10.0-browserwasm', '-p:UnoEditTargetFrameworks=net10.0-browserwasm',
        f'-p:WasmShellWebAppBasePath={base}', '-o', destination, f'-bl:{OUT}/browser.binlog')
    indexes = sorted(destination.rglob('index.html'))
    if len(indexes) != 1: raise RuntimeError(f'Expected one web entry point, found {indexes}')
    site = OUT/'site'; shutil.rmtree(site, ignore_errors=True); shutil.copytree(indexes[0].parent, site)
    (site/'.nojekyll').touch()
    revision = subprocess.check_output(['git','rev-parse','HEAD'], cwd=ROOT, text=True).strip()
    (site/'build.json').write_text(json.dumps({'name':'EditorScene','version':'0.1.0-alpha.1','revision':revision,
        'dependencyRevision':PIN,'fullActiproParity':False,'toolchain':json.loads((ROOT/'global.json').read_text())}, indent=2)+'\n')
    print('Prepared exact browser build at '+str(site), flush=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', choices=['test','desktop','native-checks','browser'])
    parser.add_argument('--configuration', choices=['Debug','Release'], default='Release')
    parser.add_argument('--base-path', default='/EditorScene/')
    parser.add_argument('--install-workloads', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'/[A-Za-z0-9._/-]*',args.base_path) or '..' in args.base_path: parser.error('Invalid base path')
    OUT.mkdir(exist_ok=True); verify_source()
    if args.install_workloads: run('dotnet','workload','install','wasm-tools','--skip-manifest-update')
    if args.target == 'test': tests(args.configuration)
    elif args.target == 'desktop': desktop(args.configuration)
    elif args.target == 'native-checks': native_checks(args.configuration)
    else: browser(args.configuration, args.base_path.rstrip('/')+'/')
if __name__ == '__main__': main()
