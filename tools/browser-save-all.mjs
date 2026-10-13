import assert from 'node:assert/strict';
import path from 'node:path';
import {readFile} from 'node:fs/promises';

export async function checkSaveAll({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  await wait(()=>globalThis.__editorSceneState?.DocumentCount===1&&!globalThis.__editorSceneState.Modified&&!globalThis.__editorSceneState.DocumentOperationBusy);
  assert.equal((await state()).CommandSurfaceEnabled['Toolbar Save All'],false);
  assert.equal((await state()).CommandSurfaceEnabled['Command Save All'],false);
  const original=(await state()).FileName;
  const contents=['first saved document 🙂\r\n', '//'+ 'a'.repeat(4093)+'🙂\r\nclass Saved {}\n'];
  const names=[];
  for(const text of contents) {
    await command('File','New'); await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
    names.push((await state()).FileName); await paste(text); await textIs(text);
  }
  await command('File','New'); await wait(()=>globalThis.__editorSceneState?.DocumentCount===4&&globalThis.__editorSceneState.OpenMenuCount===0);
  const active=(await state()).FileName; const activeRevision=(await state()).Revision;
  await wait(()=>globalThis.__editorSceneState?.CommandSurfaceEnabled['Toolbar Save All']&&globalThis.__editorSceneState.CommandSurfaceEnabled['Command Save All']);
  await page.screenshot({path:path.join(output,'save-all-enabled-dark.png')});
  const downloads=await collect(names,()=>click('Toolbar Save All'));
  for(let i=0;i<contents.length;i++)assert.equal(await readFile(downloads[i],'utf8'),contents[i]);
  let s=await state();
  assert.equal(s.FileName,active); assert.equal(s.Revision,activeRevision); assert.equal(s.DocumentCount,4);
  assert.deepEqual(s.DocumentOrder,[original,...names,active],'Saving must not append another extension to document captions');
  assert.equal(s.CommandSurfaceEnabled['Toolbar Save All'],false); assert.equal(s.CommandSurfaceEnabled['Command Save All'],false);
  assert.deepEqual(s.DocumentModified,[false,false,false,false]); await textIs('');
  checks.push('Save All downloads exact dirty filenames and bytes in tab order while preserving the active clean document and all captions');

  await click('All documents'); await wait(name=>globalThis.__editorSceneState?.VisibleCommandEnabled?.[name]===true,names[0]);
  await click('Visible command '+names[0]); await wait(name=>globalThis.__editorSceneState?.FileName===name&&globalThis.__editorSceneState.OpenMenuCount===0,names[0]);
  await textIs(contents[0]); await focusEditor(); await page.keyboard.press('Control+z'); await textIs('');
  await wait(()=>globalThis.__editorSceneState?.Modified&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Save All']);
  const single=await collect([names[0]],()=>page.keyboard.press('Control+Shift+F12'));
  assert.equal((await readFile(single[0])).length,0,'An emptied but modified document must be saved as zero bytes');
  s=await state(); assert.equal(s.FileName,names[0]); assert.equal(s.Modified,false); assert.deepEqual(s.DocumentModified,[false,false,false,false]);
  assert.deepEqual(s.DocumentOrder,[original,...names,active]);
  checks.push('Ctrl+Shift+F12 resaves an emptied dirty document under its unchanged name without resaving clean files');

  await click('Theme'); await click('Choice Light'); await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await click('Toolbar commands'); await click('Visible submenu File');
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===2&&globalThis.__editorSceneState.VisibleCommandEnabled?.['Save All']===false);
  await page.screenshot({path:path.join(output,'save-all-clean-light.png')});
  // Match native submenu lifetime: observe the child closing and returning focus
  // before sending the parent's Escape. Do not retry keys or accept an orphan.
  await page.keyboard.press('Escape');
  await wait(()=>{
    const s=globalThis.__editorSceneState;
    return s?.OpenMenuCount===1&&s.VisibleCommandEnabled?.['Save All']===undefined&&!!s.Controls?.['Visible submenu File'];
  });
  await page.keyboard.press('Escape'); await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await command('File','Close All Documents');
  await wait(()=>globalThis.__editorSceneState?.DocumentCount===1&&!globalThis.__editorSceneState.DocumentOperationBusy&&globalThis.__editorSceneState.TextLength===0);
  assert.notEqual((await state()).FileName,original);
  await click('Theme'); await click('Choice Dark'); await wait(()=>globalThis.__editorSceneState?.Theme==='Dark'); await focusEditor();
  checks.push('Clean Save All controls are disabled consistently in menus, toolbar and overflow, and saved documents close without dirty prompts');

  async function collect(expectedNames,action) {
    const files=[]; const saves=[]; const observedNames=[];
    let resolve, reject, downloadError;
    const arrived=new Promise((ok,fail)=>{resolve=ok;reject=fail;});
    arrived.catch(()=>{});
    const fail=error=>{downloadError??=error;reject(error);};
    const timeout=setTimeout(()=>fail(new Error('Expected Save All downloads did not arrive')),60000);
    const listener=download=>{
      observedNames.push(download.suggestedFilename());
      try {
        assert.equal(observedNames[observedNames.length-1],expectedNames[observedNames.length-1],'The native picker must suggest exactly one extension and no extra file');
        const file=path.join(output,`save-all-${expectedNames.length}-${files.length}.bin`); files.push(file);
        saves.push(download.saveAs(file).catch(fail));
        if(files.length===expectedNames.length)resolve();
      } catch(error) { fail(error); }
    };
    page.on('download',listener);
    try {
      await action(); await arrived;
      await wait(n=>globalThis.__editorSceneState?.SaveAllTotal===n&&globalThis.__editorSceneState.SaveAllCompleted===n&&!globalThis.__editorSceneState.SaveAllRunning&&!globalThis.__editorSceneState.DocumentOperationBusy,expectedNames.length);
      await Promise.all(saves);
      if(downloadError)throw downloadError;
      assert.deepEqual(observedNames,expectedNames); assert.equal(files.length,expectedNames.length); return files;
    } finally { clearTimeout(timeout); page.off('download',listener); }
  }
}
