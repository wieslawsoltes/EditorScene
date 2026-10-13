import assert from 'node:assert/strict';
import path from 'node:path';

export async function checkLineEndings({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  await command('File','New');
  const source='first\r\nsecond\nthird\rfourth🙂';
  await paste(source); await page.keyboard.press('Control+Home');
  await command('Advanced','Convert Line Endings to CRLF'); await textIs('first\r\nsecond\r\nthird\r\nfourth🙂');
  await page.keyboard.press('Control+z'); await textIs(source);
  await page.keyboard.press('Control+y'); await textIs('first\r\nsecond\r\nthird\r\nfourth🙂');
  await page.keyboard.press('Control+Home'); await page.keyboard.press('Shift+ArrowRight');
  const before=await state();
  await command('Advanced','Convert Line Endings to CRLF');
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0&&globalThis.__editorSceneState.FocusedControl?.endsWith('.TextArea'));
  let s=await state();
  for(const key of ['Revision','CaretOffset','SelectionStart','SelectionLength','Modified'])assert.equal(s[key],before[key],key+' changed on canonical conversion');
  await page.keyboard.press('Control+z'); await textIs(source);
  checks.push('Native line-ending conversion preserves non-delimiter text and canonical repeat commands do not add an undo unit or move selection');

  await command('Advanced','Convert Line Endings to CR');await textIs('first\rsecond\rthird\rfourth🙂');
  await wait(()=>globalThis.__editorSceneState?.LineCount===4);
  await page.keyboard.press('Control+z');await textIs(source);
  await command('Advanced','Convert Line Endings to LF');await textIs('first\nsecond\nthird\nfourth🙂');
  await page.keyboard.press('Control+z');await textIs(source);
  checks.push('All three native conversion commands preserve logical lines and undo mixed source exactly');

  await paste('one\ntwo\n');await page.keyboard.press('Control+z');await textIs(source);
  await page.keyboard.press('Control+y');await textIs('one\ntwo\n');
  await page.keyboard.press('Control+Home');await page.keyboard.type('X');await page.keyboard.press('Control+z');await textIs('one\ntwo\n');
  const redoRevision=(await state()).Revision;
  await command('Advanced','Convert Line Endings to LF');
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  assert.equal((await state()).Revision,redoRevision);
  await page.keyboard.press('Control+y');await textIs('Xone\ntwo\n');
  checks.push('A canonical LF command preserves the redo branch instead of silently replacing already-correct line breaks');

  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);
  await click('Menu Advanced');await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Convert Line Endings to CR']===false);
  s=await state();
  for(const suffix of ['CR','LF','CRLF'])assert.equal(s.VisibleCommandEnabled['Convert Line Endings to '+suffix],false);
  await page.screenshot({path:path.join(output,'line-endings-readonly-dark.png')});
  await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await click('Menu Advanced');await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Convert Line Endings to CR']===true);
  await page.screenshot({path:path.join(output,'line-endings-light.png')});
  await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await focusEditor();await textIs('Xone\ntwo\n');
  checks.push('Native line-ending menu states follow read-only permissions and render in both palettes');
}
