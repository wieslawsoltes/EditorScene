import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import path from 'node:path';

// All edits use native input. Readiness observes actual focus after menu closure,
// not just synchronous recorder state; no click/retry is used to repair focus.
export async function checkMacros({page, state, wait, textIs, paste, click, command, output, checks}) {
  const ready = expected => wait(value => {
    const s = globalThis.__editorSceneState;
    return s?.MacroState === value && s.MacroInputReady && s.OpenMenuCount === 0;
  }, expected);
  await paste('');
  await command('Macros','Start Recording');await ready('Recording');
  await page.keyboard.type('alpha');await textIs('alpha');
  await page.keyboard.press('Home');await wait(()=>globalThis.__editorSceneState?.CaretOffset===0);
  const caretBefore=(await state()).CaretOffset;await click('Editor');
  await wait(offset=>globalThis.__editorSceneState?.CaretOffset===offset,caretBefore);
  await page.keyboard.press('Shift+End');await page.keyboard.press('Control+Shift+u');await textIs('ALPHA');
  await command('Macros','Stop Recording');await ready('Idle');assert.equal((await state()).MacroHasLast,true);
  await paste('');await command('Macros','Run Last Macro');await ready('Idle');await textIs('ALPHA');
  checks.push('Macro records text/navigation/selection/transformation, ignores editor pointer input and replays native commands');
  await page.screenshot({path:path.join(output,'macro.png')});
  const macroDownload=page.waitForEvent('download');await command('Macros','Export Macro…');
  const savedMacro=await macroDownload;const macroPath=path.join(output,'recorded.esmacro');await savedMacro.saveAs(macroPath);
  const macroXml=await readFile(macroPath,'utf8');assert.ok(macroXml.includes('urn:editorscene:macro:1'));
  const macroChooser=page.waitForEvent('filechooser');await command('Macros','Import Macro…');
  await(await macroChooser).setFiles({name:'recorded.esmacro',mimeType:'application/xml',buffer:Buffer.from(macroXml)});
  await paste('');await command('Macros','Run Last Macro');await ready('Idle');await textIs('ALPHA');
  checks.push('Macro XML export/import uses real file dialogs and replays the saved command sequence');
  await command('Macros','Start Recording');await ready('Recording');
  await command('Macros','Pause Recording');await ready('Paused');
  const count=(await state()).MacroStepCount;
  await page.keyboard.press('Control+a');await page.keyboard.type('not recorded');await textIs('not recorded');
  assert.equal((await state()).MacroStepCount,count,'Paused editing must not be recorded');
  await command('Macros','Resume Recording');await ready('Recording');
  await command('Macros','Cancel Recording');await ready('Idle');
  await paste('');await command('Macros','Run Last Macro');await ready('Idle');await textIs('ALPHA');
  checks.push('Macro pause/resume/cancel permits unrecorded typing and preserves the previously saved macro');
}
