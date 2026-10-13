import assert from 'node:assert/strict';
import path from 'node:path';
import {readFile} from 'node:fs/promises';

export async function checkDocumentClosing({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  const initial=await state(); const names=[];
  const targetText='saved inactive document 🙂'; const activeText='retained active document';
  for(let i=0;i<4;i++) {
    await command('File','New');
    await wait(n=>globalThis.__editorSceneState?.DocumentCount===n&&globalThis.__editorSceneState.OpenMenuCount===0,initial.DocumentCount+i+1);
    names.push((await state()).FileName);
    if(i===0)await paste(targetText);
    if(i===2)await paste('cancelled bulk close document');
    if(i===3)await paste(activeText);
  }
  const [a,b,c,d]=names;
  const before=await state();
  await click('Document '+a,{button:'right'});
  await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Close Tabs to the Right']===true);
  assert.equal((await state()).FileName,d,'Opening an inactive tab context must not switch documents');
  await page.screenshot({path:path.join(output,'document-tab-close-dark.png')});
  await click('Visible command Close Tabs to the Right');
  await wait(name=>globalThis.__editorSceneState?.DocumentOperationBusy&&globalThis.__editorSceneState.DocumentClosePrompt?.includes(name),c);
  let s=await state();
  assert.equal(s.DocumentCount,before.DocumentCount-1,'The clean successor closes before the first dirty prompt');
  assert.ok(!s.DocumentOrder.includes(b));
  assert.equal(s.CommandSurfaceEnabled['Command Save'],false);
  assert.equal(s.CommandSurfaceEnabled['Command Close All Documents'],false);
  await click('Dialog Cancel');
  await wait(()=>!globalThis.__editorSceneState?.DocumentOperationBusy&&globalThis.__editorSceneState.DocumentClosePrompt===null);
  s=await state();
  assert.ok(s.DocumentOrder.includes(a)&&s.DocumentOrder.includes(c)&&s.DocumentOrder.includes(d));
  assert.equal(s.DocumentCount,before.DocumentCount-1); assert.equal(s.FileName,d);
  assert.equal(s.DocumentTabBuilds,before.DocumentTabBuilds);
  await textIs(activeText);
  checks.push('Close Tabs to the Right uses the clicked identity, closes clean predecessors and stops at Cancel without closing later dirty documents');

  await click('Document '+a,{button:'right'});
  await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.Close===true);
  await click('Visible command Close');
  await wait(name=>globalThis.__editorSceneState?.DocumentClosePrompt?.includes(name),a);
  const download=page.waitForEvent('download');
  await click('Dialog Save');
  await(await download).saveAs(path.join(output,'saved-inactive-document.txt'));
  await wait(n=>globalThis.__editorSceneState?.DocumentCount===n&&!globalThis.__editorSceneState.DocumentOperationBusy,before.DocumentCount-2);
  assert.equal(await readFile(path.join(output,'saved-inactive-document.txt'),'utf8'),targetText);
  s=await state(); assert.equal(s.FileName,d); assert.equal(s.Revision,before.Revision);
  assert.ok(!s.DocumentOrder.includes(a)&&s.DocumentOrder.includes(c)); await textIs(activeText);
  checks.push('Save on an inactive tab downloads that document rather than the active one and preserves the active revision and text');

  await command('File','New');
  await wait(n=>globalThis.__editorSceneState?.DocumentCount===n&&globalThis.__editorSceneState.OpenMenuCount===0,before.DocumentCount-1);
  const extra=(await state()).FileName;
  await click('Document '+d); await wait(name=>globalThis.__editorSceneState?.FileName===name,d);
  await click('Document '+extra,{button:'middle'});
  await wait(n=>globalThis.__editorSceneState?.DocumentCount===n&&!globalThis.__editorSceneState.DocumentOperationBusy,before.DocumentCount-2);
  assert.equal((await state()).FileName,d); await textIs(activeText);
  assert.ok(!(await state()).DocumentOrder.includes(extra));
  checks.push('Real middle-button input closes only its inactive clean tab without activating or rebinding it');

  await focusEditor(); await page.keyboard.press('Alt+-');
  await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Close All But This']===true);
  assert.equal((await state()).VisibleCommandEnabled['Close Tabs to the Right'],false,'Last tab has no right-hand targets');
  await page.keyboard.press('Escape'); await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await click('Theme'); await click('Choice Light'); await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await focusEditor(); await page.keyboard.press('Alt+-');
  await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Close All But This']===true);
  await page.screenshot({path:path.join(output,'document-tab-close-light.png')});
  const menuBuilds=(await state()).DocumentTabMenuBuilds;
  assert.equal(menuBuilds,initial.DocumentTabMenuBuilds+1,'One native context menu is retained across target changes and themes');
  await click('Visible command Close All But This');
  // All documents in this test session are synthetic. Discard through real native
  // dialogs, with a finite bound and an observed count change after each decision.
  async function discardPendingBatch() {
    const limit=(await state()).DocumentCount+2;
    for(let i=0;i<limit;i++) {
      await wait(()=>!globalThis.__editorSceneState?.DocumentOperationBusy||globalThis.__editorSceneState.DocumentClosePrompt!==null);
      const current=await state(); if(!current.DocumentOperationBusy)return;
      assert.ok(current.DocumentClosePrompt);
      assert.ok(!current.DocumentClosePrompt.includes(d),'Close All But This must not prompt for the retained target');
      await click('Dialog Discard');
      await wait(n=>!globalThis.__editorSceneState?.DocumentOperationBusy||globalThis.__editorSceneState.DocumentCount<n,current.DocumentCount);
    }
    throw new Error('Bulk close did not finish within the original membership bound');
  }
  await discardPendingBatch();
  await wait(name=>globalThis.__editorSceneState?.DocumentCount===1&&globalThis.__editorSceneState.FileName===name,d);
  await textIs(activeText);
  await focusEditor(); await page.keyboard.press('Control+z'); await textIs('');
  await page.keyboard.press('Control+y'); await textIs(activeText);
  await page.keyboard.press('Control+z'); await textIs('');
  checks.push('Keyboard tab menu and Close All But This retain one native menu, preserve the chosen dirty document and keep its undo/redo history');

  await command('File','New'); await wait(()=>globalThis.__editorSceneState?.DocumentCount===2&&globalThis.__editorSceneState.OpenMenuCount===0);
  const dirty=(await state()).FileName; await paste('last dirty document');
  await command('File','New'); await wait(()=>globalThis.__editorSceneState?.DocumentCount===3&&globalThis.__editorSceneState.OpenMenuCount===0);
  const last=(await state()).FileName;
  await command('File','Close All Documents');
  await wait(name=>globalThis.__editorSceneState?.DocumentClosePrompt?.includes(name),dirty);
  assert.equal((await state()).DocumentCount,2);
  await click('Dialog Cancel'); await wait(()=>!globalThis.__editorSceneState?.DocumentOperationBusy);
  assert.deepEqual((await state()).DocumentOrder,[dirty,last]);
  await focusEditor(); await page.keyboard.press('Control+F4');
  await wait(name=>globalThis.__editorSceneState?.DocumentCount===1&&globalThis.__editorSceneState.FileName===name&&!globalThis.__editorSceneState.DocumentOperationBusy,dirty);
  await textIs('last dirty document');
  await page.keyboard.press('Control+F4');
  await wait(name=>globalThis.__editorSceneState?.DocumentClosePrompt?.includes(name),dirty);
  await click('Dialog Discard');
  await wait(()=>globalThis.__editorSceneState?.DocumentCount===1&&globalThis.__editorSceneState.TextLength===0&&!globalThis.__editorSceneState.DocumentOperationBusy);
  s=await state(); assert.notEqual(s.FileName,dirty); assert.equal(s.Modified,false); assert.equal(s.DocumentMenuCheckCount,1);
  assert.equal(s.DocumentTabMenuBuilds,menuBuilds);
  await command('File','Close Other Documents');
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0&&!globalThis.__editorSceneState.DocumentOperationBusy);
  assert.equal((await state()).DocumentCount,1);
  await click('Theme'); await click('Choice Dark'); await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await focusEditor();
  checks.push('File Close All stops at Cancel, Ctrl+F4 uses dirty prompts, closing the last document creates exactly one clean placeholder and empty Others is harmless');
}
