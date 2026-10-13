import assert from 'node:assert/strict';
import path from 'node:path';

// Native modal input only. No direct caret, document or validation setters.
export async function checkLineNavigation({page,state,wait,textIs,paste,click,fill,command,focusEditor,output,checks}) {
  await command('File','New');
  const text='first\n\t🙂z\nlast'; await paste(text); await page.keyboard.press('Control+Home');
  const revision=(await state()).Revision;
  async function open(keyboard=false) {
    if(keyboard)await page.keyboard.press('Control+g');else await command('Edit','Go To Line…');
    await wait(()=>globalThis.__editorSceneState?.GoToLineOpen&&!!globalThis.__editorSceneState.Controls['Go to line input']);
  }
  async function input(value) {
    await fill('Go to line input',value);
    await wait(text=>globalThis.__editorSceneState?.GoToLineText===text,value);
  }
  await open(); await input('0'); await click('Dialog Go');
  await wait(()=>globalThis.__editorSceneState?.GoToLineOpen&&!globalThis.__editorSceneState.GoToLineValid&&globalThis.__editorSceneState.GoToLineError.length>0);
  assert.equal((await state()).CaretOffset,0);assert.equal((await state()).Revision,revision);
  await input('2:-1'); await click('Dialog Go');await wait(()=>globalThis.__editorSceneState?.GoToLineOpen&&!globalThis.__editorSceneState.GoToLineValid);
  await input('2:3'); await click('Dialog Go');await wait(()=>globalThis.__editorSceneState?.GoToLineError.includes('Unicode'));
  await page.screenshot({path:path.join(output,'go-to-line-validation-dark.png')});
  await input('2:4');await wait(()=>globalThis.__editorSceneState?.GoToLineValid);
  await page.keyboard.press('Enter');
  await wait(()=>!globalThis.__editorSceneState?.GoToLineOpen&&globalThis.__editorSceneState.CaretOffset===9&&globalThis.__editorSceneState.SelectionLength===0&&globalThis.__editorSceneState.FocusedControl?.endsWith('.TextArea'));
  await textIs(text);assert.equal((await state()).Revision,revision);
  assert.equal((await state()).Controls['Go to line input'],undefined,'Closed modal input must not remain in the observation registry');
  checks.push('Go To Line keeps invalid ranges and split-surrogate positions open for correction, then Enter navigates without editing');

  await page.keyboard.press('Control+Home');for(let i=0;i<3;i++)await page.keyboard.press('Shift+ArrowRight');
  const before=await state();await open(true);await input('3:2');await click('Dialog Cancel');
  await wait(()=>!globalThis.__editorSceneState?.GoToLineOpen&&globalThis.__editorSceneState.FocusedControl?.endsWith('.TextArea'));
  let s=await state();assert.equal(s.CaretOffset,before.CaretOffset);assert.equal(s.SelectionStart,before.SelectionStart);assert.equal(s.SelectionLength,before.SelectionLength);
  await page.keyboard.type('Z');await textIs('Zst\n\t🙂z\nlast');await page.keyboard.press('Control+z');await textIs(text);
  checks.push('Ctrl+G and Cancel retain the original native selection and restore immediate typing and undo');

  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);
  await open();await input('3:2');await click('Dialog Go');
  await wait(()=>!globalThis.__editorSceneState?.GoToLineOpen&&globalThis.__editorSceneState.CaretOffset===12);
  await textIs(text);assert.equal((await state()).ReadOnly,true);
  const originalViewport=page.viewportSize();await page.setViewportSize({width:440,height:700});
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await open();await input('2:4');
  s=await state();const box=s.Controls['Go to line input'];assert.ok(box[0]>=0&&box[0]+box[2]<=441&&box[1]>=0&&box[1]+box[3]<=701,'Native navigation input must fit the narrow viewport');
  await page.screenshot({path:path.join(output,'go-to-line-light-narrow.png')});
  await click('Dialog Cancel');await wait(()=>!globalThis.__editorSceneState?.GoToLineOpen);
  await page.setViewportSize(originalViewport);
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await focusEditor();await textIs(text);
  checks.push('Native Go To Line remains usable in readonly views and a narrow light-themed window');
}
