import assert from 'node:assert/strict';
import path from 'node:path';

// Real keyboard/pointer paths. The bridge supplies observations, never mutations.
export async function checkReview({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  await paste('aaaa');await page.keyboard.press('Control+Home');
  await page.keyboard.press('Shift+ArrowRight');await page.keyboard.press('Shift+ArrowRight');
  await page.keyboard.press('Control+d');await wait(()=>globalThis.__editorSceneState?.SelectionCount===2);
  await page.keyboard.type('x');await textIs('xx');await page.keyboard.press('Control+z');await textIs('aaaa');
  checks.push('Adjacent occurrence selections remain independent through native typing and one undo');

  await paste('one one one');await page.keyboard.press('Control+End');await page.keyboard.press('Control+Shift+ArrowLeft');
  await wait(()=>globalThis.__editorSceneState?.SelectionStart===8 && globalThis.__editorSceneState?.SelectionLength===3);
  await page.keyboard.press('Control+d');await wait(()=>globalThis.__editorSceneState?.SelectionStart===0 && globalThis.__editorSceneState?.SelectionCount===2);
  await page.keyboard.press('Control+d');await wait(()=>globalThis.__editorSceneState?.SelectionStart===4 && globalThis.__editorSceneState?.SelectionCount===3);
  await page.keyboard.type('x');await textIs('x x x');
  checks.push('Ctrl+D wraps to an earlier occurrence and retains the actual primary caret');

  await paste('ab ab ab');await page.keyboard.press('Control+Home');
  await page.keyboard.press('Shift+ArrowRight');await page.keyboard.press('Shift+ArrowRight');
  await command('Edit','Select All Occurrences');await wait(()=>globalThis.__editorSceneState?.SelectionCount===3);
  // Menu closure restores focus; a pointer click would deliberately clear extra selections.
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0 && globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  await page.keyboard.type('z');await textIs('z z z');
  checks.push('Select All Occurrences is available in the native menu and edits every match');

  await paste('([])');await page.keyboard.press('Control+Home');
  await wait(()=>globalThis.__editorSceneState?.BracketPairs===2);
  await command('Edit','Go To Matching Bracket');await wait(()=>globalThis.__editorSceneState?.CaretOffset===3);
  await command('Edit','Select To Matching Bracket');await wait(()=>globalThis.__editorSceneState?.SelectionStart===0 && globalThis.__editorSceneState?.SelectionLength===3);
  assert.equal((await state()).TextPrefix,'([])');
  checks.push('Matching-bracket navigation and selection use current analysis without changing text');

  const before=await state();
  if(!before.Whitespace)await command('View','Visible Whitespace');
  if(before.IndentGuides)await command('View','Indentation Guides');
  if(!before.Ruler)await command('View','Column Ruler');
  const original=await state();
  for(let i=0;i<6;i++) {
    await click('Toolbar New');await wait(n=>globalThis.__editorSceneState?.DocumentCount===n,original.DocumentCount+1);
    const opened=await state();assert.ok(opened.Whitespace && !opened.IndentGuides && opened.Ruler,'View options must survive rebinding');
    await click('Close document');await wait(n=>globalThis.__editorSceneState?.DocumentCount===n,original.DocumentCount);
    const restored=await state();assert.equal(restored.IconCount,original.IconCount);assert.equal(restored.RegisteredTabCount,original.RegisteredTabCount);
    assert.equal(restored.FileName,original.FileName);
  }
  await page.screenshot({path:path.join(output,'review-options.png')});
  checks.push('Repeated document open/close preserves view options without growing retained icons or tab registrations');
  // Leave preferences as the caller found them, so the rest of the suite is independent.
  if(!before.Whitespace)await command('View','Visible Whitespace');
  if(before.IndentGuides)await command('View','Indentation Guides');
  if(!before.Ruler)await command('View','Column Ruler');
  await focusEditor();
}
