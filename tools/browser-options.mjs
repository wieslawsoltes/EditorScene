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
  async function reveal(name) {
    await wait(key=>!!globalThis.__editorSceneState?.Controls?.[key]&&!!globalThis.__editorSceneState.Controls['Visible Editor options scroll'],name);
    // Native wheel movement can be scaled/coalesced. Drive bounded scrolling
    // from observed geometry, not the assumption that one wheel delta is an exact
    // layout-pixel displacement. Every input and its final visibility are checked.
    for(let step=0;step<8;step++) {
      const c=(await state()).Controls,input=c[name],viewport=c['Visible Editor options scroll'];
      assert.ok(input[2]<=viewport[2]+1&&input[3]<viewport[3],'Option input must fit the scroll viewport');
      assert.ok(input[0]>=viewport[0]-1&&input[0]+input[2]<=viewport[0]+viewport[2]+1,'Option input must not be clipped horizontally');
      if(input[1]>=viewport[1]&&input[1]+input[3]<=viewport[1]+viewport[3])return;
      const gap=input[1]<viewport[1]?input[1]-viewport[1]:input[1]+input[3]-viewport[1]-viewport[3];
      const delta=Math.sign(gap)*Math.max(120,Math.abs(gap)+40);
      await page.mouse.move(viewport[0]+viewport[2]-4,viewport[1]+Math.min(100,viewport[3]/2));
      await page.mouse.wheel(0,delta);
      await wait(({key,previousY})=>{
        const a=globalThis.__editorSceneState?.Controls?.[key];return a&&Math.abs(a[1]-previousY)>=1;
      },{key:name,previousY:input[1]},5000);
    }
    const c=(await state()).Controls,a=c[name],v=c['Visible Editor options scroll'];
    assert.ok(a[1]>=v[1]&&a[1]+a[3]<=v[1]+v[3],`Could not reveal ${name} after eight actual scroll inputs`);
  }
  async function number(name,value) {
    const key='Option input '+name;await reveal(key);await click(key);
    await page.keyboard.press('Control+a');await page.keyboard.type(String(value));await page.keyboard.press('Tab');
  }
  async function toggle(name) { await reveal('Visible '+name);await click('Visible '+name); }
  async function reset() {
    await wait(()=>{
      const c=globalThis.__editorSceneState?.Controls,r=c?.['Visible Restore editor option defaults'],v=c?.['Visible Editor options scroll'],apply=c?.['Dialog Apply'];
      return r&&v&&apply&&r[0]>=0&&r[1]>=v[1]+v[3]-1&&r[1]+r[3]<=apply[1];
    });
    const r=(await state()).Controls['Visible Restore editor option defaults'];
    assert.ok(r[0]+r[2]<=page.viewportSize().width,'Restore Defaults must not be clipped horizontally');
    await click('Visible Restore editor option defaults');
  }
  await open();await number('Tab and indent size',8);await toggle('Insert spaces instead of tabs');
  await page.screenshot({path:path.join(output,'editor-options-dark.png')});
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.FocusedControl?.endsWith('.TextArea'));
  assert.equal((await state()).TabSize,original.TabSize);assert.equal((await state()).ConvertTabsToSpaces,original.ConvertTabsToSpaces);
  await textIs('\tX');
  await page.keyboard.type('Y');await textIs('\tXY');await page.keyboard.press('Control+z');await textIs('\tX');
  checks.push('Native options Cancel preserves the draft target and returns focus for real typing and undo');
  await open();await number('Tab and indent size',8);await number('Ruler column',80);
  await toggle('Insert spaces instead of tabs');await toggle('Show column ruler');
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
  await open();await number('Tab and indent size',2);await reset();
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  assert.equal((await state()).TabSize,8);
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await open();await page.screenshot({path:path.join(output,'editor-options-light.png')});
  await reset();await click('Dialog Apply');
  await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.TabSize===4&&!globalThis.__editorSceneState.ConvertTabsToSpaces&&globalThis.__editorSceneState.RulerColumn===120);
  await textIs('\tX');
  checks.push('Restore Defaults is always visible, remains a draft until Apply, and native options render under both themes');
  await open();const originalViewport=page.viewportSize();await page.setViewportSize({width:440,height:760});
  await wait(()=>{
    const c=globalThis.__editorSceneState?.Controls;const d=c?.['Visible Editor display options'],e=c?.['Visible Editor indentation options'];
    return d&&e&&e[1]>d[1]+d[3]-1&&Math.abs(e[0]-d[0])<2;
  });
  const panel=(await state()).Controls['Visible Editor option sections'];
  assert.ok(panel[0]>=0&&panel[0]+panel[2]<=440,'Narrow options must not overflow horizontally');
  await number('Tab and indent size',3);
  await page.screenshot({path:path.join(output,'editor-options-narrow.png')});
  await reset(); // The fixed footer stays reachable even after scrolling down.
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  assert.equal((await state()).TabSize,4);
  await page.setViewportSize(originalViewport);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  checks.push('Narrow options stack existing sections, allow real scrolling and numeric input, and cancel without applying');
}
