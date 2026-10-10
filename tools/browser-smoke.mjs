#!/usr/bin/env node
// Tests the exact published Uno app through real user input; the application state bridge is read-only.
import assert from 'node:assert/strict';
import {createReadStream} from 'node:fs';
import {mkdir, stat, readFile, writeFile} from 'node:fs/promises';
import {createServer} from 'node:http';
import path from 'node:path';
import {chromium} from 'playwright';
const root = path.resolve(process.argv[2] ?? 'artifacts/site');
const base = '/EditorScene/';
const output = path.resolve('artifacts/browser-tests');
await mkdir(output, {recursive:true});
const mime = {'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.wasm':'application/wasm','.json':'application/json','.png':'image/png','.svg':'image/svg+xml','.woff2':'font/woff2','.ttf':'font/ttf'};
const server = createServer(async (req,res) => {
  try {
    const url = new URL(req.url,'http://127.0.0.1');
    if (!url.pathname.startsWith(base)) { res.writeHead(404); res.end('Wrong application base path'); return; }
    let relative = decodeURIComponent(url.pathname.slice(base.length));
    if (!relative || relative.endsWith('/')) relative += 'index.html';
    const target = path.resolve(root,relative);
    if (!target.startsWith(root+path.sep)) { res.writeHead(403); res.end(); return; }
    const info = await stat(target); if (!info.isFile()) throw new Error('Not a file');
    res.writeHead(200,{'Content-Type':mime[path.extname(target)] ?? 'application/octet-stream','Content-Length':info.size,'Cache-Control':'no-store'});
    if(req.method==='HEAD') res.end(); else createReadStream(target).on('error',()=>res.destroy()).pipe(res);
  } catch { res.writeHead(404); res.end('Not found'); }
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const checks=[], log=[], exceptions=[], badResponses=[]; const metrics={};
let browser,context,page,error,failureState;
const start=performance.now();
try {
  browser=await chromium.launch({args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  context=await browser.newContext({viewport:{width:1280,height:800},acceptDownloads:true});
  await context.grantPermissions(['clipboard-read','clipboard-write'],{origin});
  await context.addInitScript(()=>{
    globalThis.__editorGraphics=[];
    const original=HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext=function(...args){
      const result=original.apply(this,args);
      if(result && typeof args[0]==='string' && /webgl|webgpu/.test(args[0])) {
        let renderer=''; try { renderer=result.getParameter?.(result.RENDERER) ?? ''; const ext=result.getExtension?.('WEBGL_debug_renderer_info'); if(ext)renderer=result.getParameter(ext.UNMASKED_RENDERER_WEBGL); }catch{}
        globalThis.__editorGraphics.push({api:args[0],renderer});
      }
      return result;
    };
  });
  await context.tracing.start({screenshots:true,snapshots:true,sources:true});
  page=await context.newPage();
  page.on('console',m=>log.push(`${m.type()}: ${m.text()}`));
  page.on('pageerror',e=>{exceptions.push(e.stack??e.message);log.push('pageerror: '+e.stack);});
  page.on('response',r=>{if(r.status()>=400){badResponses.push(`${r.status()} ${r.url()}`);log.push(badResponses.at(-1));}});
  page.on('requestfailed',r=>log.push(`requestfailed: ${r.url()} ${r.failure()?.errorText}`));
  page.on('dialog',async d=>d.accept(d.defaultValue()||'document.txt'));
  const state=()=>page.evaluate(()=>globalThis.__editorSceneState);
  const wait=async(predicate,arg)=>{await page.waitForFunction(predicate,arg,{timeout:60000});const s=await state();assert.equal(s.Error,null,s.Error??'Application error');return s;};
  const textIs=text=>wait(t=>globalThis.__editorSceneState?.TextPrefix===t,text);
  const stableBounds=async name=>{
    // A canvas control cannot use Playwright's DOM bounding-box actionability.
    // Require unchanged bounds in three distinct native observations, not three
    // polls of one stale object. Filtering can resize a ContentDialog after the
    // keyboard API returns. This observer changes no application state.
    await page.evaluate(()=>{globalThis.__editorScenePointerProbe={lastState:null,key:'',samples:0,since:0};});
    await wait(n=>{
      const state=globalThis.__editorSceneState, probe=globalThis.__editorScenePointerProbe;
      if (!state || probe.lastState===state) return false;
      probe.lastState=state;
      const box=state.Controls?.[n];
      if (!box || box.length!==4 || !box.every(Number.isFinite) || box[2]<=0 || box[3]<=0) {probe.samples=0;probe.key='';return false;}
      const key=JSON.stringify(box);
      if (probe.key!==key) {probe.key=key;probe.samples=1;probe.since=performance.now();return false;}
      probe.samples++;
      return probe.samples>=3 && performance.now()-probe.since>=200;
    },name);
    return (await state()).Controls[name];
  };
  const click=async name=>{const b=await stableBounds(name);await page.mouse.click(b[0]+b[2]/2,b[1]+b[3]/2);};
  const focusEditor=async()=>{const b=await stableBounds('Editor');await page.mouse.click(b[0]+100,b[1]+10);};
  const paste=async text=>{await focusEditor();await page.keyboard.press('Escape');await page.keyboard.press('Control+a');await page.evaluate(t=>navigator.clipboard.writeText(t),text);await page.keyboard.press('Control+v');await textIs(text);};
  const fill=async(name,text)=>{await click(name);await page.keyboard.press('Control+a');await page.keyboard.type(text);};
  const command=async(menu,label)=>{await click('Menu '+menu);await click('Command '+label);};
  const response=await page.goto(origin+base+'?test=1',{waitUntil:'domcontentloaded'});assert.equal(response.status(),200);
  await page.waitForFunction(()=>globalThis.__editorSceneState?.Ready,null,{timeout:180000}); metrics.startupMs=performance.now()-start;
  let s=await state();assert.ok(s.Controls.Editor[2]>300 && s.Controls.Editor[3]>200);
  checks.push('Published Uno WebAssembly app starts beneath /EditorScene/');
  await focusEditor();await page.keyboard.type('alpha');await page.keyboard.press('Enter');await page.keyboard.type('beta');await textIs('alpha\nbeta');
  await page.keyboard.press('Home');await page.keyboard.press('Shift+End');await page.keyboard.type('second');await textIs('alpha\nsecond');
  checks.push('Typing, newline and keyboard selection replacement');
  await page.keyboard.press('Control+a');await page.keyboard.press('Backspace');await textIs('');await page.keyboard.press('Control+z');await textIs('alpha\nsecond');await page.keyboard.press('Control+y');await textIs('');
  checks.push('Select-all, delete, undo and redo');
  const unicode='A\t🙂e\u0301\nمرحبا 日本語';await paste(unicode);await page.keyboard.press('Control+Home');await page.keyboard.press('ArrowRight');await page.keyboard.press('ArrowRight');await page.keyboard.press('Delete');await textIs('A\te\u0301\nمرحبا 日本語');await page.keyboard.press('Delete');await textIs('A\t\nمرحبا 日本語');
  checks.push('Unicode clipboard, surrogate and combining-sequence deletion');await page.screenshot({path:path.join(output,'unicode.png')});
  await paste('one two one');await click('Toolbar Replace');await wait(()=>globalThis.__editorSceneState?.SearchOpen);
  await fill('PART_searchTextBox','one');await wait(()=>globalThis.__editorSceneState?.SearchResultCount===2);
  await fill('PART_replaceTextBox','item');await click('PART_replaceAll');await textIs('item two item');await click('PART_close');await focusEditor();await page.keyboard.press('Control+z');await textIs('one two one');
  checks.push('Visible find/replace fields, replace-all and atomic undo');
  await paste('using System; class C { void M() { Console.Wri } }');for(let i=0;i<4;i++)await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('Control+Space');await wait(()=>globalThis.__editorSceneState?.CompletionOpen && globalThis.__editorSceneState.CompletionLabels.includes('WriteLine'));
  await page.screenshot({path:path.join(output,'completion.png')});await page.keyboard.type('teLine');await page.keyboard.press('Tab');await wait(()=>globalThis.__editorSceneState?.TextPrefix.includes('Console.WriteLine'));
  checks.push('Compiler-backed C# completion shown and committed through native popup');
  await page.keyboard.type('(');await page.keyboard.press('Control+Shift+Space');await wait(()=>globalThis.__editorSceneState?.SignatureOpen);await page.keyboard.press('Escape');checks.push('Real overload signature help');
  const valid='using System;\nclass Example\n{\n    public void Run()\n    {\n        Console.WriteLine("Hello");\n    }\n}\n';
  await paste(valid);await wait(()=>globalThis.__editorSceneState?.AnalysisRevision===globalThis.__editorSceneState?.Revision && globalThis.__editorSceneState.Foldings>=2);
  s=await state();assert.equal(s.Diagnostics.length,0,JSON.stringify(s.Diagnostics));assert.ok(s.Symbols.some(n=>n.includes('Example')));
  checks.push('C# semantic diagnostics, navigation symbols and syntax-based outlining');
  await click('Toolbar Collapse all');await wait(()=>globalThis.__editorSceneState?.Folded>0);await click('Toolbar Expand all');await wait(()=>globalThis.__editorSceneState?.Folded===0);checks.push('Collapse and expand native folded regions');
  await click('Document outline');await page.screenshot({path:path.join(output,'light-editor.png')});
  await click('Toolbar Toggle bookmark');await wait(()=>globalThis.__editorSceneState?.BookmarkCount===1);
  await click('Toolbar Split editor');await wait(()=>globalThis.__editorSceneState?.Split);await focusEditor();await page.keyboard.press('Control+End');await page.keyboard.type('// shared');
  await wait(()=>globalThis.__editorSceneState?.SecondaryTextPrefix===globalThis.__editorSceneState?.TextPrefix && globalThis.__editorSceneState.TextPrefix.endsWith('// shared'));
  checks.push('Shared-document split view and anchored bookmark indicator');await click('Toolbar Split editor');await wait(()=>globalThis.__editorSceneState?.Split===false);
  await click('Theme');await click('Choice Dark');
  await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');await page.screenshot({path:path.join(output,'dark-editor.png')});checks.push('Theme changes update native editor and application chrome');
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);const before=(await state()).TextPrefix;await focusEditor();await page.keyboard.type('BLOCKED');await textIs(before);await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);checks.push('Read-only user input protection');
  await paste('for');await page.keyboard.press('Tab');await wait(()=>globalThis.__editorSceneState?.SnippetActive && globalThis.__editorSceneState.SnippetFields.length===2);
  s=await state();assert.equal(s.TextPrefix.slice(s.SelectionStart,s.SelectionStart+s.SelectionLength),'i');
  await page.keyboard.type('row');await wait(()=>globalThis.__editorSceneState?.TextPrefix.includes('int row = 0; row < length; row++'));
  await page.keyboard.press('Tab');await wait(()=>globalThis.__editorSceneState?.SelectionLength===6);await page.keyboard.type('count');
  await page.keyboard.press('Shift+Tab');await wait(()=>{const s=globalThis.__editorSceneState;return s?.TextPrefix.slice(s.SelectionStart,s.SelectionStart+s.SelectionLength)==='row';});
  await page.screenshot({path:path.join(output,'snippet.png')});await page.keyboard.press('Enter');await wait(()=>globalThis.__editorSceneState?.SnippetActive===false);
  checks.push('Snippet shortcut, linked fields, Tab/Shift+Tab navigation and Enter completion');
  await paste('for');await page.keyboard.press('Tab');await wait(()=>globalThis.__editorSceneState?.SnippetActive);await page.keyboard.press('Escape');await page.keyboard.press('Control+z');await textIs('for');checks.push('Snippet insertion and shortcut removal undo atomically');
  await paste('');
  await page.keyboard.press('Control+k');await page.keyboard.press('Control+x');
  await click('Visible Filter snippets');await page.keyboard.type('property');
  await click('Dialog Insert');await wait(()=>globalThis.__editorSceneState?.SnippetActive && globalThis.__editorSceneState.TextPrefix.includes('MyProperty'));
  await page.keyboard.press('Enter');await wait(()=>globalThis.__editorSceneState?.SnippetActive===false);
  checks.push('Native snippet picker filtering and insertion through the keyboard command');
  const snippetXml='<CodeSnippets xmlns="http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet"><CodeSnippet Format="1.0.0"><Header><Title>Browser Imported</Title><Shortcut>custom</Shortcut></Header><Snippet><Declarations><Literal><ID>value</ID><Default>imported</Default></Literal></Declarations><Code Language="CSharp"><![CDATA[$value$-$value$$end$]]></Code></Snippet></CodeSnippet></CodeSnippets>';
  const snippetChooser=page.waitForEvent('filechooser');await command('IntelliPrompt','Import Snippets…');
  await(await snippetChooser).setFiles({name:'browser-custom.snippet',mimeType:'application/xml',buffer:Buffer.from(snippetXml)});
  await paste('custom');await page.keyboard.press('Tab');await wait(()=>globalThis.__editorSceneState?.SnippetActive && globalThis.__editorSceneState.TextPrefix==='imported-imported');
  await page.keyboard.press('Enter');await wait(()=>globalThis.__editorSceneState?.SnippetActive===false);
  checks.push('Actual Visual Studio snippet file import and shortcut expansion');
  const snippetDownload=page.waitForEvent('download');await command('IntelliPrompt','Export Snippets…');
  const exported=await snippetDownload;await exported.saveAs(path.join(output,'exported.snippet'));
  assert.ok((await readFile(path.join(output,'exported.snippet'),'utf8')).includes('Browser Imported'));
  checks.push('Actual snippet catalog export includes imported definitions');
  const json='{\n  "name": "EditorScene 🙂",\n  "value": 42\n}';
  const chooser=page.waitForEvent('filechooser');await click('Toolbar Open');await(await chooser).setFiles({name:'browser-input.json',mimeType:'application/json',buffer:Buffer.from(json)});
  await wait(()=>globalThis.__editorSceneState?.FileName==='browser-input.json' && globalThis.__editorSceneState.Language==='json');await textIs(json);
  await wait(()=>globalThis.__editorSceneState?.AnalysisRevision===globalThis.__editorSceneState?.Revision);assert.equal((await state()).Diagnostics.length,0);checks.push('Actual file upload, UTF-8 decoding and automatic JSON language selection');
  const downloaded=page.waitForEvent('download');await click('Toolbar Save');const saved=await downloaded;await saved.saveAs(path.join(output,'saved-input.json'));assert.equal(await readFile(path.join(output,'saved-input.json'),'utf8'),json);checks.push('Actual browser save download preserves document bytes');
  await command('File','100,000-line document');await wait(()=>globalThis.__editorSceneState?.LineCount===100000);
  await focusEditor();const large=performance.now();await page.keyboard.press('Control+End');await wait(()=>globalThis.__editorSceneState?.CaretOffset===globalThis.__editorSceneState?.TextLength);
  s=await state();assert.ok(s.CachedLines<=256);assert.ok(s.VisibleLines>0 && s.VisibleLines<100);metrics.largeDocumentEndNavigationMs=performance.now()-large;
  checks.push('100,000-line Unicode document with bounded layout cache and viewport-only rendering');await page.screenshot({path:path.join(output,'large-document.png')});
  await page.setViewportSize({width:640,height:700});await wait(()=>globalThis.__editorSceneState?.Controls?.Editor?.[2]<650);await page.screenshot({path:path.join(output,'narrow-editor.png')});checks.push('Narrow window resize');
  metrics.graphics=await page.evaluate(()=>globalThis.__editorGraphics);assert.ok(metrics.graphics.length>0,'The app did not create a modern graphics context');checks.push('Real Skia WebGL context created (CI uses software SwiftShader, not a physical GPU claim)');
  assert.deepEqual(exceptions,[],'Unhandled browser exceptions');assert.deepEqual(badResponses,[],'Published assets must not return HTTP errors');
  await writeFile(path.join(output,'final-state.json'),JSON.stringify(await state(),null,2));
} catch(caught) {
  error=caught;
  if(page){
    await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});
    failureState=await page.evaluate(()=>({state:globalThis.__editorSceneState,graphics:globalThis.__editorGraphics,title:document.title,body:document.body?.innerText,active:document.activeElement?.outerHTML})).catch(()=>null);
    await writeFile(path.join(output,'failure-state.json'),JSON.stringify(failureState,null,2));
  }
} finally {
  await writeFile(path.join(output,'console.log'),log.join('\n'));
  await writeFile(path.join(output,'results.json'),JSON.stringify({passed:!error,checks,metrics,error:error?.stack,browser:browser?.version(),publishRoot:root,base},null,2));
  await context?.tracing.stop({path:path.join(output,'trace.zip')}).catch(()=>{});await browser?.close();await new Promise(resolve=>server.close(resolve));
}
console.log(JSON.stringify({passed:!error,checks,metrics,error:error?.stack},null,2));
if(error){console.error('Failure state: '+JSON.stringify(failureState));console.error(log.slice(-30).join('\n'));process.exitCode=1;}
