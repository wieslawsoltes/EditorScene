import assert from 'node:assert/strict';
import path from 'node:path';

export async function checkStructuralSelection({page,state,wait,textIs,paste,command,output,checks}) {
  const text='class C { int M() => 1 + 2; }';
  await paste(text);await page.keyboard.press('Control+Home');
  const offset=text.indexOf('1');for(let i=0;i<offset;i++)await page.keyboard.press('ArrowRight');
  await wait(n=>globalThis.__editorSceneState?.CaretOffset===n,offset);
  await page.keyboard.press('Alt+Shift+ArrowRight');
  await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===1&&!globalThis.__editorSceneState?.SelectionExpansionPending);
  let s=await state();assert.equal(s.TextPrefix.slice(s.SelectionStart,s.SelectionStart+s.SelectionLength),'1');
  await page.keyboard.press('Alt+Shift+ArrowRight');
  await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===2);
  s=await state();assert.equal(s.TextPrefix.slice(s.SelectionStart,s.SelectionStart+s.SelectionLength),'1 + 2');
  await page.screenshot({path:path.join(output,'structural-selection.png')});
  checks.push('Alt+Shift+Right expands through actual C# syntax tokens and expressions');
  await page.keyboard.press('Alt+Shift+ArrowLeft');await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===1);
  await page.keyboard.press('Alt+Shift+ArrowLeft');
  await wait(n=>globalThis.__editorSceneState?.CaretOffset===n&&globalThis.__editorSceneState?.SelectionLength===0&&globalThis.__editorSceneState?.SelectionExpansionDepth===0,offset);
  await textIs(text);checks.push('Alt+Shift+Left contracts to the exact original caret without editing');
  await command('Edit','Expand Selection');
  await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===1&&globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  await command('Edit','Contract Selection');await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===0&&globalThis.__editorSceneState?.SelectionLength===0);
  checks.push('Native Edit menu structural commands restore keyboard focus and preserve selection history');
  await page.keyboard.press('ArrowLeft');await page.keyboard.press('Alt+Shift+ArrowLeft');
  s=await state();assert.equal(s.SelectionExpansionDepth,0);assert.equal(s.SelectionLength,0);
  await textIs(text);checks.push('Manual keyboard movement clears the old structural selection history');
}

export async function checkLspSelection({page,state,wait,textIs,focusEditor,command,fixture,checks,before}) {
  await focusEditor();await page.keyboard.press('Control+Home');for(let i=0;i<8;i++)await page.keyboard.press('ArrowRight');
  await wait(()=>globalThis.__editorSceneState?.CaretOffset===8);
  const previous=fixture.journal.filter(e=>e.method==='textDocument/selectionRange').length;
  await command('Edit','Expand Selection');await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===1);
  let s=await state();assert.equal(s.TextPrefix.slice(s.SelectionStart,s.SelectionStart+s.SelectionLength),'Demo');
  await command('Edit','Expand Selection');await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===2);
  s=await state();assert.equal(s.SelectionLength,before.length);
  await command('Edit','Contract Selection');await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===1);
  await command('Edit','Contract Selection');await wait(()=>globalThis.__editorSceneState?.SelectionExpansionDepth===0&&globalThis.__editorSceneState?.SelectionLength===0);
  assert.equal(fixture.journal.filter(e=>e.method==='textDocument/selectionRange').length,previous+1);
  await textIs(before);checks.push('Negotiated WebSocket LSP selection ranges expand/contract natively with one cached request');
}
