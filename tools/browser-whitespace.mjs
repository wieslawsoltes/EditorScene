import assert from 'node:assert/strict';
import path from 'node:path';

// Uses native menus, selection keys and clipboard input, never a document mutation bridge.
export async function checkWhitespace({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  await command('File','New');
  assert.equal((await state()).TabSize,4,'The preceding options tests must restore the default tab width');
  async function whitespace(label, screenshot) {
    await click('Menu Advanced');await click('Visible submenu Whitespace');
    await wait(name=>!!globalThis.__editorSceneState?.Controls?.['Visible command '+name],label);
    const box=(await state()).Controls['Visible command '+label],size=page.viewportSize();
    assert.ok(box[0]>=0&&box[1]>=0&&box[0]+box[2]<=size.width+1&&box[1]+box[3]<=size.height+1,'Whitespace command must be on screen');
    if(screenshot)await page.screenshot({path:path.join(output,screenshot)});
    await click('Visible command '+label);
    await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  }
  await paste('ready');await page.keyboard.press('Control+End');
  let unchanged=(await state()).Revision;
  await whitespace('Convert Selected Spaces to Tabs');
  await wait(()=>globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  assert.equal((await state()).Revision,unchanged);
  await page.keyboard.type('!');await textIs('ready!');await page.keyboard.press('Control+z');await textIs('ready');
  await paste('\tvalue');await page.keyboard.press('Control+End');unchanged=(await state()).Revision;
  await whitespace('Tabify Selected Lines');
  await wait(()=>globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  assert.equal((await state()).Revision,unchanged);
  await page.keyboard.type('!');await textIs('\tvalue!');await page.keyboard.press('Control+z');await textIs('\tvalue');
  checks.push('No-op whitespace menu actions return keyboard focus without changing revisions and allow immediate typing/undo');
  const source='\tfirst\tvalue\n\tsecond\tvalue\n';
  await paste(source);await page.keyboard.press('Control+Home');await page.keyboard.press('Shift+End');
  await whitespace('Convert Selected Tabs to Spaces','whitespace-menu-dark.png');
  await textIs('    first   value\n\tsecond\tvalue\n');
  await wait(()=>globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  await page.keyboard.press('Control+z');await textIs(source);
  await page.keyboard.press('Control+y');await textIs('    first   value\n\tsecond\tvalue\n');
  checks.push('Selected tab conversion changes inline whitespace only inside the selection and supports single-step native undo/redo');
  await paste('    first   value\n    second   value\n');await page.keyboard.press('Control+Home');await page.keyboard.press('Shift+End');
  await whitespace('Tabify Selected Lines');await textIs('\tfirst   value\n    second   value\n');
  checks.push('Line tabification changes selected indentation without altering inline spaces or the next line');
  await paste('\tX\tY');await page.keyboard.press('Control+Home');
  await page.keyboard.press('ArrowRight');await page.keyboard.press('ArrowRight');await page.keyboard.press('Shift+ArrowRight');
  await whitespace('Convert Selected Tabs to Spaces');await textIs('\tX   Y');
  checks.push('A partial selection calculates tab stops from the unselected line prefix instead of resetting its column');
  await paste('\tfirst\n\tsecond');await page.keyboard.press('Control+Home');
  await whitespace('Untabify Selected Lines');await textIs('    first\n\tsecond');
  await paste('a  \r\nb\t\rc  ');await page.keyboard.press('Control+Home');
  await whitespace('Trim Trailing Whitespace');await textIs('a\r\nb\rc');
  checks.push('No-selection line conversion affects the current line, while trailing trim preserves mixed original line endings');
  await paste('a   b');await page.keyboard.press('Control+a');
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await whitespace('Convert Selected Spaces to Tabs','whitespace-menu-light.png');await textIs('a\tb');
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);
  const before=(await state()).Revision;
  await whitespace('Convert Selected Tabs to Spaces');
  await wait(()=>globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  await page.keyboard.type('BLOCKED');await textIs('a\tb');assert.equal((await state()).Revision,before);
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  await focusEditor();
  checks.push('Whitespace submenu renders in both palettes and read-only commands return focus without permitting input');
}
