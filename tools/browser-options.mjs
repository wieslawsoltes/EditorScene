import assert from 'node:assert/strict';
import path from 'node:path';
import {checkSoftTabs} from './browser-soft-tabs.mjs';

// Real menus, numeric inputs, keyboard and native dialog buttons; no mutation bridge.
export async function checkEditorOptions({page,state,wait,textIs,paste,click,command,output,checks}) {
  await command('File','New');
  await paste('\tX');await page.keyboard.press('Control+End');
  await wait(()=>globalThis.__editorSceneState?.StatusPosition.includes('Col 6')&&globalThis.__editorSceneState.StatusPosition.includes('Ch 3'));
  const original=await state();
  async function settled(width,stacked=null) {
    // A layout can already be stacked BEFORE resize. Require the requested native
    // root width, containment AND stable geometry across distinct observations.
    // This does not retry input or relax the overflow assertion that caught the race.
    await page.evaluate(()=>{globalThis.__editorOptionsLayoutProbe={last:null,key:'',samples:0,since:0};});
    return wait(({width,stacked})=>{
      const s=globalThis.__editorSceneState,p=globalThis.__editorOptionsLayoutProbe;
      if(!s||!s.OptionsDialogOpen||p.last===s)return false;
      p.last=s;
      const c=s.Controls,b=c?.['Status bar'],v=c?.['Visible Editor options scroll'],a=c?.['Visible Editor option sections'],
        d=c?.['Visible Editor display options'],e=c?.['Visible Editor indentation options'];
      const valid=[b,v,a,d,e].every(r=>r&&r.length===4&&r.every(Number.isFinite)&&r[2]>0&&r[3]>0);
      const fits=valid&&Math.abs(b[2]-width)<1&&[v,a,d,e].every(r=>r[0]>=0&&r[0]+r[2]<=width);
      const layout=valid&&(stacked===null||(stacked?e[1]>=d[1]+d[3]-1&&Math.abs(e[0]-d[0])<2:e[0]>=d[0]+d[2]-1&&Math.abs(e[1]-d[1])<2));
      if(!fits||!layout){p.key='';p.samples=0;return false;}
      const key=JSON.stringify([b,v,a,d,e]);
      if(key!==p.key){p.key=key;p.samples=1;p.since=performance.now();return false;}
      return ++p.samples>=3&&performance.now()-p.since>=200;
    },{width,stacked});
  }
  async function open() {
    await command('View','Editor Options…');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen);
    await settled(page.viewportSize().width);
  }
  async function reveal(name) {
    await wait(key=>!!globalThis.__editorSceneState?.Controls?.[key]&&!!globalThis.__editorSceneState.Controls['Visible Editor options scroll'],name);
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
      },{key:name,previousY:input[1]});
    }
    const c=(await state()).Controls,a=c[name],v=c['Visible Editor options scroll'];
    assert.ok(a[1]>=v[1]&&a[1]+a[3]<=v[1]+v[3],`Could not reveal ${name} after eight actual scroll inputs`);
  }
  async function number(name,value) {
    const key='Option input '+name;await reveal(key);await click(key);
    // Native focus/select-all and TextChanged are asynchronous. The old helper
    // sent both digits before selection synchronization completed (trace: "80"
    // became "0"). Observe the real focused input, never inject/retry text.
    await page.evaluate(()=>{globalThis.__numberFocusProbe={last:null,count:0};});
    await wait(label=>{
      const s=globalThis.__editorSceneState,p=globalThis.__numberFocusProbe;
      if(s===p.last)return false;p.last=s;
      if(!s?.OptionInputs?.[label]?.Focused){p.count=0;return false;}
      return ++p.count>=2;
    },name);
    await page.keyboard.press('Control+a');
    await wait(label=>{const n=globalThis.__editorSceneState?.OptionInputs?.[label];return n?.Focused&&n.SelectionStart===0&&n.SelectionLength===n.Text.length;},name);
    let entered='';
    for(const digit of String(value)) {
      entered+=digit;await page.keyboard.type(digit);
      await wait(({name,entered})=>{const n=globalThis.__editorSceneState?.OptionInputs?.[name];return n?.Focused&&n.Text===entered&&n.SelectionLength===0&&n.SelectionStart===entered.length;},{name,entered});
    }
    await page.keyboard.press('Tab');
    await wait(({name,text})=>globalThis.__editorSceneState?.OptionInputs?.[name]?.Text===text,{name,text:String(value)});
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
  await open();await settled(page.viewportSize().width,false);
  checks.push('Desktop options expose side-by-side Display and Tabs/IntelliPrompt sections rather than an unnecessarily narrow scrolling form');
  await number('Tab and indent size',8);await toggle('Insert spaces instead of tabs');
  await page.screenshot({path:path.join(output,'editor-options-dark.png')});
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.FocusedControl?.endsWith('.TextArea'));
  assert.equal((await state()).TabSize,original.TabSize);assert.equal((await state()).ConvertTabsToSpaces,original.ConvertTabsToSpaces);
  await textIs('\tX');
  await page.keyboard.type('Y');await textIs('\tXY');await page.keyboard.press('Control+z');await textIs('\tX');
  checks.push('Native options Cancel preserves the draft target and returns focus for real typing and undo');
  await open();await number('Ruler column',0);await click('Dialog Apply');
  await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen&&globalThis.__editorSceneState.OptionsValidation.length>0);
  assert.equal((await state()).RulerColumn,original.RulerColumn,'Invalid numeric draft must not be silently clamped/applied');
  await number('Tab and indent size',8);await number('Ruler column',80);
  await toggle('Insert spaces instead of tabs');await toggle('Show column ruler');
  await click('Dialog Apply');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false&&globalThis.__editorSceneState.TabSize===8&&globalThis.__editorSceneState.ConvertTabsToSpaces&&globalThis.__editorSceneState.RulerColumn===80);
  await page.keyboard.press('Control+End');await page.keyboard.press('Tab');await textIs('\tX       ');
  await page.keyboard.press('Control+z');await textIs('\tX');
  await wait(()=>globalThis.__editorSceneState?.StatusPosition.includes('Col 10')&&globalThis.__editorSceneState.StatusPosition.includes('Ch 3'));
  checks.push('Applied tab stops drive actual Tab input and tab-aware Col/UTF-16 Ch status with atomic undo');
  await checkSoftTabs({page,state,wait,textIs,paste,open,toggle,click,checks});
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);
  await click('Editor');await page.keyboard.press('Tab');await textIs('\tX');
  assert.equal((await state()).Error,null,'Read-only Tab must not report an editing exception');
  await page.keyboard.press('Control+End');await page.keyboard.press('Backspace');await textIs('\tX');
  assert.equal((await state()).Error,null,'Read-only Backspace must not report an editing exception');
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  checks.push('Configured Tab and Backspace in a read-only native view preserve text without reporting an operation failure');
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
  await settled(440,true);
  const panel=(await state()).Controls['Visible Editor option sections'];
  assert.ok(panel[0]>=0&&panel[0]+panel[2]<=440,'Narrow options must not overflow horizontally');
  await number('Tab and indent size',3);
  for(const width of [1280,440,520,1280,440]) {
    await page.setViewportSize({width,height:760});await settled(width,width<584);
  }
  await reveal('Option input Tab and indent size');await click('Option input Tab and indent size');
  await wait(()=>globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextBox'));
  assert.equal(await page.evaluate(()=>document.getElementById('uno-input')?.value),'3','Resizing must preserve the actual numeric input draft');
  await page.screenshot({path:path.join(output,'editor-options-narrow.png')});
  await reset();
  await click('Dialog Cancel');await wait(()=>globalThis.__editorSceneState?.OptionsDialogOpen===false);
  assert.equal((await state()).TabSize,4);
  await page.setViewportSize(originalViewport);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  checks.push('Repeated desktop/narrow resizes preserve the numeric draft, contained layout, real scrolling and Cancel behavior');
}
