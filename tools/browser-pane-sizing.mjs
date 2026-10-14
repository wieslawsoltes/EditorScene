import assert from 'node:assert/strict';
import path from 'node:path';

// Only real native mouse/keyboard/menu input mutates the application. Diagnostics
// are read-only observations of the actual border/Thumb layout, not test models.
export async function checkPaneSizing({page,state,wait,click,focusEditor,output,checks}) {
  const viewport=page.viewportSize();
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0&&globalThis.__editorSceneState.AnalysisRevision===globalThis.__editorSceneState.Revision);
  const original=await state();
  const outlineWasOpen=!!original.Controls['Outline pane'];
  if(!outlineWasOpen)await click('Document outline');
  if(!original.ToolPaneOpen)await click('Error List');
  await wait(()=>!!globalThis.__editorSceneState?.Controls?.['Resize document outline']&&!!globalThis.__editorSceneState.Controls['Resize tool window']);
  const extent=async(name,dimension)=>(await state()).Controls[name][dimension];
  const extentIs=(name,dimension,expected)=>wait(({name,dimension,expected})=>Math.abs((globalThis.__editorSceneState?.Controls?.[name]?.[dimension]??-10000)-expected)<2,{name,dimension,expected});
  async function gripFocus(name) {
    await click(name);
    await wait(()=>globalThis.__editorSceneState?.FocusedControl==='Microsoft.UI.Xaml.Controls.Primitives.Thumb');
  }
  async function drag(name,dx,dy,cancel=false) {
    const b=(await state()).Controls[name],x=b[0]+b[2]/2,y=b[1]+b[3]/2;
    await page.mouse.move(x,y);await page.mouse.down();
    await page.mouse.move(x+dx,y+dy,{steps:8});
    if(cancel)await page.keyboard.press('Escape');
    await page.mouse.up();
  }
  await gripFocus('Resize document outline');await page.keyboard.press('Enter');await extentIs('Outline pane',2,240);
  await drag('Resize document outline',-50,0);await extentIs('Outline pane',2,290);
  await page.keyboard.press('ArrowLeft');await extentIs('Outline pane',2,300);
  const savedOutline=await extent('Outline pane',2);
  await drag('Resize document outline',-35,0,true);await extentIs('Outline pane',2,savedOutline);
  checks.push('Native outline grip supports exact pointer resizing, keyboard adjustment and Escape rollback without changing the document');

  await gripFocus('Resize tool window');await page.keyboard.press('Enter');await extentIs('Bottom tool pane',3,200);
  await page.keyboard.press('ArrowUp');await extentIs('Bottom tool pane',3,210);
  await page.keyboard.press('ArrowDown');await extentIs('Bottom tool pane',3,200);
  await drag('Resize tool window',0,-45);await extentIs('Bottom tool pane',3,245);
  await drag('Resize tool window',0,-30,true);await extentIs('Bottom tool pane',3,245);
  checks.push('Native bottom tool grip supports pointer and keyboard resizing with cancellable drag and default reset');

  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await page.screenshot({path:path.join(output,'tool-panes-light.png')});
  await extentIs('Outline pane',2,savedOutline);await extentIs('Bottom tool pane',3,245);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await page.screenshot({path:path.join(output,'tool-panes-dark.png')});
  await gripFocus('Resize document outline');await page.keyboard.press('End');
  await wait(()=>globalThis.__editorSceneState?.Controls?.['Outline pane']?.[2]>500);
  await gripFocus('Resize tool window');await page.keyboard.press('End');
  await wait(()=>globalThis.__editorSceneState?.Controls?.['Bottom tool pane']?.[3]>300);
  const wideOutline=await extent('Outline pane',2),wideBottom=await extent('Bottom tool pane',3);
  await page.setViewportSize({width:640,height:480});
  await wait(()=>{
    const c=globalThis.__editorSceneState?.Controls;
    return c?.['Status bar']?.[2]===640&&c?.['Bottom tool pane']?.[3]<300&&c?.['Outline pane']?.[2]<400;
  });
  const small=(await state()).Controls;
  for(const name of ['Outline pane','Bottom tool pane','Resize document outline','Resize tool window']) {
    const b=small[name];assert.ok(b&&b[0]>=0&&b[1]>=0&&b[0]+b[2]<=641&&b[1]+b[3]<=481,`${name} must stay inside a constrained viewport`);
  }
  assert.ok(small.Editor[2]>=120&&small.Editor[3]>=40,'Both tool panes must leave usable central editor space');
  await page.screenshot({path:path.join(output,'tool-panes-constrained.png')});
  await page.setViewportSize(viewport);
  await wait(width=>globalThis.__editorSceneState?.Controls?.['Status bar']?.[2]===width,viewport.width);
  await extentIs('Outline pane',2,wideOutline);await extentIs('Bottom tool pane',3,wideBottom);
  checks.push('Tool pane sizes clamp inside narrow short windows and restore both preferred extents when the viewport grows');

  await click('Hide Document Outline');await wait(()=>globalThis.__editorSceneState?.Controls?.['Outline pane']===undefined);
  await click('Hide bottom tool window');await wait(()=>globalThis.__editorSceneState?.ToolPaneOpen===false);
  await click('Document outline');await extentIs('Outline pane',2,wideOutline);
  await click('Error List');await extentIs('Bottom tool pane',3,wideBottom);
  await click('Events');await extentIs('Bottom tool pane',3,wideBottom);
  await click('Error List');await extentIs('Bottom tool pane',3,wideBottom);
  await gripFocus('Resize document outline');await page.keyboard.press('Home');await extentIs('Outline pane',2,120);
  await page.keyboard.press('Enter');await extentIs('Outline pane',2,240);
  await gripFocus('Resize tool window');await page.keyboard.press('Home');await extentIs('Bottom tool pane',3,120);
  await page.keyboard.press('Enter');await extentIs('Bottom tool pane',3,200);
  if(!outlineWasOpen)await click('Hide Document Outline');
  if(!original.ToolPaneOpen)await click('Hide bottom tool window');
  if(original.Theme!=='Dark') {
    await click('Theme');await click('Choice '+(original.Theme==='High Contrast'?'High contrast':original.Theme));
    await wait(theme=>globalThis.__editorSceneState?.Theme===theme,original.Theme);
  }
  const current=await state();
  for(const key of ['FileName','Revision','TextPrefix','DocumentTabBuilds','DocumentCount']) assert.equal(current[key],original[key],key+' changed during tool-pane resizing');
  checks.push('Distinct native pane close controls retain extents through hide/reopen, tool switching and themes without rebuilding tabs or editing text');
  await focusEditor();
  await checkOverflowDismissal();

  async function checkOverflowDismissal() {
    const before=await state();
    for(const theme of ['Light','Dark']) {
      await click('Theme');await click('Choice '+theme);await wait(t=>globalThis.__editorSceneState?.Theme===t,theme);
      await click('Toolbar commands');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===1);
      await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
      for(const group of ['File','Edit','IntelliPrompt','Formatting','Search','Navigation','Macros']) {
        await click('Toolbar commands');await click('Visible submenu '+group);
        await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===2&&Object.keys(globalThis.__editorSceneState.VisibleCommandEnabled).length>0);
        await page.keyboard.press('Escape');
        await wait(g=>{
          const s=globalThis.__editorSceneState;
          return s?.OpenMenuCount===1&&Object.keys(s.VisibleCommandEnabled).length===0&&!!s.Controls?.['Visible submenu '+g];
        },group);
        await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
        const after=await state();
        for(const key of ['FileName','Revision','TextPrefix','DocumentTabBuilds','DocumentCount','Modified']) assert.equal(after[key],before[key],`${theme}/${group}: ${key} changed during Escape`);
        for(const key of ['Command Undo','Command Redo']) assert.equal(after.CommandSurfaceEnabled[key],before.CommandSurfaceEnabled[key],`${theme}/${group}: ${key} changed during Escape`);
      }
      checks.push(`Native root-only and all seven overflow child/root Escape pairs preserve documents and undo history in ${theme}`);
    }
    if(before.Theme!=='Dark') {
      await click('Theme');await click('Choice '+(before.Theme==='High Contrast'?'High contrast':before.Theme));
      await wait(t=>globalThis.__editorSceneState?.Theme===t,before.Theme);
    }
    await focusEditor();
  }
}
