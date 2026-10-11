import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import path from 'node:path';

// All edits go through user input. Pass browser values explicitly to serialized predicates.
export async function checkMacros({page, state, wait, textIs, paste, click, command, output, checks}) {
  await paste('');
  await command('Macros','Start Recording');await wait(()=>globalThis.__editorSceneState?.MacroState==='Recording');
  await page.keyboard.type('alpha');await textIs('alpha');
  const caretBefore=(await state()).CaretOffset;await click('Editor');
  await wait(offset=>globalThis.__editorSceneState?.CaretOffset===offset,caretBefore);
  await page.keyboard.press('Home');await page.keyboard.press('Shift+End');await page.keyboard.press('Control+Shift+u');await textIs('ALPHA');
  await command('Macros','Stop Recording');await wait(()=>globalThis.__editorSceneState?.MacroState==='Idle' && globalThis.__editorSceneState.MacroHasLast);
  await paste('');await command('Macros','Run Last Macro');await textIs('ALPHA');
  checks.push('Macro records text/navigation/selection/transformation, ignores editor pointer input and replays native commands');
  await page.screenshot({path:path.join(output,'macro.png')});
  const macroDownload=page.waitForEvent('download');await command('Macros','Export Macro…');
  const savedMacro=await macroDownload;const macroPath=path.join(output,'recorded.esmacro');await savedMacro.saveAs(macroPath);
  const macroXml=await readFile(macroPath,'utf8');assert.ok(macroXml.includes('urn:editorscene:macro:1'));
  const macroChooser=page.waitForEvent('filechooser');await command('Macros','Import Macro…');
  await(await macroChooser).setFiles({name:'recorded.esmacro',mimeType:'application/xml',buffer:Buffer.from(macroXml)});
  await paste('');await command('Macros','Run Last Macro');await textIs('ALPHA');
  checks.push('Macro XML export/import uses real file dialogs and replays the saved command sequence');
  await command('Macros','Start Recording');await command('Macros','Pause Recording');
  await wait(()=>globalThis.__editorSceneState?.MacroState==='Paused');await page.keyboard.type('not recorded');
  await command('Macros','Resume Recording');await wait(()=>globalThis.__editorSceneState?.MacroState==='Recording');
  await command('Macros','Cancel Recording');await wait(()=>globalThis.__editorSceneState?.MacroState==='Idle');
  await paste('');await command('Macros','Run Last Macro');await textIs('ALPHA');
  checks.push('Macro pause/resume/cancel preserves the previously saved macro');
}
