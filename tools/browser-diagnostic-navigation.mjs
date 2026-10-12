import assert from 'node:assert/strict';
import path from 'node:path';

export async function checkDiagnosticNavigation({page,state,wait,textIs,click,fill,focusEditor,output,checks,text}) {
  let s=await state();const offsets=s.DiagnosticGrid.VisibleOffsets;
  assert.ok(offsets.length>=3,'Navigation requires actual compiler diagnostics');
  const revision=s.Revision,projections=s.DiagnosticProjectionBuilds;
  await focusEditor();await page.keyboard.press('F8');
  await wait(n=>globalThis.__editorSceneState?.SelectionStart===n&&globalThis.__editorSceneState.DiagnosticGrid.SelectedOffset===n,offsets[0]);
  await page.keyboard.press('Shift+F8');
  await wait(n=>globalThis.__editorSceneState?.SelectionStart===n&&globalThis.__editorSceneState.DiagnosticGrid.SelectedOffset===n,offsets.at(-1));
  await page.keyboard.press('F8');await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,offsets[0]);
  await click('Next diagnostic');await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,offsets[1]);
  await click('Previous diagnostic');await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,offsets[0]);
  assert.equal((await state()).Revision,revision);assert.equal((await state()).DiagnosticProjectionBuilds,projections);
  await textIs(text);checks.push('F8, Shift+F8 and native previous/next buttons traverse and wrap current diagnostics without editing');

  await click('Sort diagnostics by Line');await click('Sort diagnostics by Line');
  await wait(()=>globalThis.__editorSceneState?.DiagnosticGrid.SortColumn==='Line'&&globalThis.__editorSceneState.DiagnosticGrid.SortDirection==='Descending');
  s=await state();const descending=s.DiagnosticGrid.VisibleOffsets,selected=s.DiagnosticGrid.SelectedOffset;
  const index=descending.indexOf(selected);assert.ok(index>=0,'Sorting must retain diagnostic row identity');
  // Focus the Error List itself rather than the editor; keyboard navigation must
  // bubble to its same handler without invoking an internal command bridge.
  await click('Filter diagnostics');await page.keyboard.press('F8');
  await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,descending[(index+1)%descending.length]);
  await fill('Filter diagnostics','U299');await wait(()=>globalThis.__editorSceneState?.DiagnosticRowCount===1);
  s=await state();const filtered=s.DiagnosticGrid.VisibleOffsets[0];
  await page.keyboard.press('F8');await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,filtered);
  await click('Previous diagnostic');await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,filtered);
  await page.screenshot({path:path.join(output,'diagnostic-navigation.png')});
  checks.push('Diagnostic navigation from the Error List follows changed sort/filter order and wraps a single remaining result');

  await fill('Filter diagnostics','NO_SUCH_DIAGNOSTIC');await wait(()=>globalThis.__editorSceneState?.DiagnosticRowCount===0);
  s=await state();const caret=s.CaretOffset;
  await page.keyboard.press('F8');await page.keyboard.press('Shift+F8');
  assert.equal((await state()).CaretOffset,caret);await textIs(text);
  await click('Clear diagnostic filter');await wait(()=>globalThis.__editorSceneState?.DiagnosticRowCount===300);
  await click('Reset diagnostic columns and order');await wait(()=>globalThis.__editorSceneState?.DiagnosticGrid.SortColumn==='ProviderOrder');
  // Return the list to its first row through actual navigation, avoiding a stale
  // scroll offset for the independent column/navigation tests which follow.
  await focusEditor();await page.keyboard.press('F8');
  await wait(n=>globalThis.__editorSceneState?.SelectionStart===n,offsets[0]);
  checks.push('Empty filtered diagnostic results ignore F8 without moving the source caret and recover after clearing');
}
