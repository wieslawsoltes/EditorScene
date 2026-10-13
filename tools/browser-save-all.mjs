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
  const downloads=await collect(2,()=>click('Toolbar Save All'),2);
  for(let i=0;i<contents.length;i++)assert.equal(await readFile(downloads[i],'utf8'),contents[i]);
  let s=await state();
  assert.equal(s.FileName,active); assert.equal(s.Revision,activeRevision); assert.equal(s.DocumentCount,4);
  assert.equal(s.CommandSurfaceEnabled['Toolbar Save All'],false); assert.equal(s.CommandSurfaceEnabled['Command Save All'],false);
  assert.deepEqual(s.DocumentModified,[false,false,false,false]); await textIs('');
  checks.push('Save All writes only dirty documents in tab order, including a boundary surrogate, while retaining the active clean document and revision');

  await click('All documents'); await wait(name=>globalThis.__editorSceneState?.VisibleCommandEnabled?.[name]===true,names[0]);
  await click('Visible command '+names[0]); await wait(name=>globalThis.__editorSceneState?.FileName===name&&globalThis.__editorSceneState.OpenMenuCount===0,names[0]);
  await textIs(contents[0]); await focusEditor(); await page.keyboard.press('Control+z'); await textIs('');
  await wait(()=>globalThis.__editorSceneState?.Modified&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Save All']);
  const single=await collect(1,()=>page.keyboard.press('Control+Shift+F12'),1);
  assert.equal((await readFile(single[0])).length,0,'An emptied but modified document must be saved as zero bytes');
  s=await state(); assert.equal(s.FileName,names[0]); assert.equal(s.Modified,false); assert.deepEqual(s.DocumentModified,[false,false,false,false]);
  checks.push('Ctrl+Shift+F12 saves the one newly dirty document after undo, including an empty result, without resaving clean files');

  await click('Theme'); await click('Choice Light'); await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await click('Toolbar commands'); await click('Visible submenu File');
  await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Save All']===false);
  await page.screenshot({path:path.join(output,'save-all-clean-light.png')});
  await page.keyboard.press('Escape'); await page.keyboard.press('Escape'); await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await command('File','Close All Documents');
  await wait(()=>globalThis.__editorSceneState?.DocumentCount===1&&!globalThis.__editorSceneState.DocumentOperationBusy&&globalThis.__editorSceneState.TextLength===0);
  assert.notEqual((await state()).FileName,original);
  await click('Theme'); await click('Choice Dark'); await wait(()=>globalThis.__editorSceneState?.Theme==='Dark'); await focusEditor();
  checks.push('Clean Save All controls are disabled consistently in menus, toolbar and overflow, and saved documents close without dirty prompts');

  async function collect(expected,action,planned) {
    const files=[]; const saves=[];
    let resolve, reject;
    const arrived=new Promise((ok,fail)=>{resolve=ok;reject=fail;});
    const timeout=setTimeout(()=>reject(new Error('Expected Save All downloads did not arrive')),60000);
    const listener=download=>{
      const file=path.join(output,`save-all-${planned}-${files.length}.bin`); files.push(file);
      saves.push(download.saveAs(file));
      if(files.length===expected)resolve();
    };
    page.on('download',listener);
    try {
      await action(); await arrived;
      await wait(n=>globalThis.__editorSceneState?.SaveAllTotal===n&&globalThis.__editorSceneState.SaveAllCompleted===n&&!globalThis.__editorSceneState.SaveAllRunning&&!globalThis.__editorSceneState.DocumentOperationBusy,planned);
      await Promise.all(saves); assert.equal(files.length,expected); return files;
    } finally { clearTimeout(timeout); page.off('download',listener); }
  }
}
