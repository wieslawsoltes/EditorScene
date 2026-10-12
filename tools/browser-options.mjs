import assert from 'node:assert/strict';
import path from 'node:path';

// Real menus, numeric inputs, keyboard and native dialog buttons; no mutation bridge.
export async function checkEditorOptions({page,state,wait,textIs,paste,click,command,output,checks}) {
  await command('File','New');
  await paste('\tX');await page.keyboard.press('Control+End');
  await wait(()=>globalThis.__editorSceneState?.StatusPosition.includes('Col 6')&&globalThis.__editorSceneState.StatusPosition.includes('Ch 3'));
  const original=await state();
  async function open() {
    await command('View','Editor Options…');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen);
  }
  async function number(name,value) {
    await click('Option input '+name);await page.keyboard.press('Control+a');await page.keyboard.type(String(value));await page.keyboard.press('Tab');
  }
  await open();await number('Tab and indent size',8);await click('Visible Insert spaces instead of tabs');
  await page.screenshot({path:path.join(output,'editor-options-dark.png')});
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.FocusedControl?.endsWith('.TextArea'));
  assert.equal((await state()).TabSize,original.TabSize);assert.equal((await state()).ConvertTabsToSpaces,original.ConvertTabsToSpaces);
  await textIs('\tX');
  await page.keyboard.type('Y');await textIs('\tXY');await page.keyboard.press('Control+z');await textIs('\tX');
  checks.push('Native options Cancel preserves the draft target and returns focus for real typing and undo');
  await open();await number('Tab and indent size',8);await number('Ruler column',80);
  await click('Visible Insert spaces instead of tabs');await click('Visible Show column ruler');
  await click('Dialog Apply');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.TabSize===8&&globalThis.__editorSceneState.ConvertTabsToSpaces&&globalThis.__editorSceneState.RulerColumn===80);
  await page.keyboard.press('Control+End');await page.keyboard.press('Tab');await textIs('\tX       ');
  await page.keyboard.press('Control+z');await textIs('\tX');
  await wait(()=>globalThis.__editorSceneState?.StatusPosition.includes('Col 10')&&globalThis.__editorSceneState.StatusPosition.includes('Ch 3'));
  checks.push('Applied tab stops drive actual Tab input and tab-aware Col/UTF-16 Ch status with atomic undo');
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);
  await click('Editor');await page.keyboard.press('Tab');await textIs('\tX');
  assert.equal((await state()).Error,null,'Read-only Tab must not report an editing exception');
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  checks.push('Configured Tab in a read-only native view preserves text without reporting an operation failure');
  await command('Window','Split Editor');await wait(()=>globalThis.__editorSceneState?.Split&&globalThis.__editorSceneState.SecondaryTabSize===8&&globalThis.__editorSceneState.SecondaryConvertTabsToSpaces&&globalThis.__editorSceneState.SecondaryRulerColumn===80);
  await command('Window','Split Editor');await wait(()=>globalThis.__editorSceneState?.Split===false);
  checks.push('New native split views inherit configured tab conversion and ruler position');
  await open();await number('Tab and indent size',2);await click('Visible Restore editor option defaults');
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  assert.equal((await state()).TabSize,8);
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await open();await page.screenshot({path:path.join(output,'editor-options-light.png')});
  await click('Visible Restore editor option defaults');await click('Dialog Apply');
  await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.TabSize===4&&!globalThis.__editorSceneState.ConvertTabsToSpaces&&globalThis.__editorSceneState.RulerColumn===120);
  await textIs('\tX');
  checks.push('Restore Defaults remains a draft until Apply and native options render under both editor themes');
  await open();const originalViewport=page.viewportSize();await page.setViewportSize({width:440,height:760});
  await wait(()=>{
    const c=globalThis.__editorSceneState?.Controls;const d=c?.['Visible Editor display options'],e=c?.['Visible Editor indentation options'];
    return d&&e&&e[1]>d[1]+d[3]-1&&Math.abs(e[0]-d[0])<2;
  });
  let controls=(await state()).Controls;const panel=controls['Visible Editor option sections'];
  assert.ok(panel[0]>=0&&panel[0]+panel[2]<=440,'Narrow options must not overflow horizontally');
  await page.mouse.move(Math.min(350,panel[0]+100),220);await page.mouse.wheel(0,700);
  await wait(()=>{
    const c=globalThis.__editorSceneState?.Controls;const input=c?.['Option input Tab and indent size'],cancel=c?.['Dialog Cancel'];
    return input&&cancel&&input[1]>60&&input[1]+input[3]<cancel[1];
  });
  await number('Tab and indent size',3);
  await page.screenshot({path:path.join(output,'editor-options-narrow.png')});
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  assert.equal((await state()).TabSize,4);
  await page.setViewportSize(originalViewport);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  checks.push('Narrow options stack existing sections, allow real scrolling and numeric input, and cancel without applying');
}
