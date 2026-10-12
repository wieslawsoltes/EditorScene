import assert from 'node:assert/strict';

// Exercise the actual key route and native preference dialog, never a mutation bridge.
export async function checkSoftTabs({page,state,wait,textIs,paste,open,toggle,click,checks}) {
  async function replace(text) { await page.keyboard.press('Control+a'); await paste(text); }
  async function caret(offset) {
    await page.keyboard.press('Control+Home');
    for(let i=0;i<offset;i++) await page.keyboard.press('ArrowRight');
    await wait(n=>globalThis.__editorSceneState?.CaretOffset===n&&globalThis.__editorSceneState.SelectionLength===0,offset);
  }
  await replace('          word'); await caret(10); await page.keyboard.press('Backspace');
  await textIs('        word'); assert.equal((await state()).CaretOffset,8);
  await page.keyboard.press('Control+z'); await textIs('          word');
  await replace('\t   word'); await caret(4); await page.keyboard.press('Backspace');
  await textIs('\tword'); assert.equal((await state()).CaretOffset,1);
  await page.keyboard.press('Control+z'); await textIs('\t   word');
  checks.push('Actual Backspace reaches preceding stops in spaces and mixed-tab indentation with one Undo');

  await open(); await toggle('Backspace over spaces to tab stop'); await click('Dialog Apply');
  await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  await caret(4); await page.keyboard.press('Backspace'); await textIs('\t  word');
  await page.keyboard.press('Control+z'); await textIs('\t   word');
  await open(); await toggle('Backspace over spaces to tab stop'); await click('Dialog Cancel');
  await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  await caret(4); await page.keyboard.press('Backspace'); await textIs('\t  word');
  await page.keyboard.press('Control+z'); await textIs('\t   word');
  await open(); await toggle('Backspace over spaces to tab stop'); await click('Dialog Apply');
  await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  checks.push('Native soft-tab checkbox Apply changes Backspace behavior while Cancel preserves the disabled preference');

  await replace('x    '); await page.keyboard.press('Control+End'); await page.keyboard.press('Backspace');
  await textIs('x   ');
  await replace('a\r\nb'); await page.keyboard.press('Control+End'); await page.keyboard.press('ArrowLeft');
  await wait(()=>globalThis.__editorSceneState?.CaretOffset===3);
  await page.keyboard.press('Backspace'); await textIs('ab');
  checks.push('Configured soft-tab Backspace preserves ordinary content-space deletion and CRLF line joining');

  await replace('      x\n      x'); await caret(6);
  await page.keyboard.press('Shift+ArrowRight'); await page.keyboard.press('Control+d');
  await wait(()=>globalThis.__editorSceneState?.SelectionCount===2);
  await page.keyboard.press('Backspace'); await textIs('      \n      ');
  await page.keyboard.press('Backspace'); await textIs('\n');
  await page.keyboard.press('Control+z'); await textIs('      \n      ');
  await page.keyboard.press('Control+z'); await textIs('      x\n      x');
  checks.push('Keyboard-created multiple carets use atomic soft-tab Backspace and undo each whole operation');
  await replace('\tX'); await page.keyboard.press('Control+End'); await textIs('\tX');
}
