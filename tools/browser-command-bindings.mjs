import assert from 'node:assert/strict';
import path from 'node:path';

// Observe real native control states; change the application only through user input.
export async function checkCommandBindings({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  await command('File','New');
  await wait(()=>globalThis.__editorSceneState?.TextLength===0&&globalThis.__editorSceneState.CommandSurfaceEnabled?.['Toolbar Undo']===false);
  let s=await state();
  assert.equal(s.CommandSurfaceEnabled['Toolbar Copy'],false);
  assert.equal(s.CommandSurfaceEnabled['Toolbar Cut'],false);
  assert.equal(s.CommandSurfaceEnabled['Toolbar Indent'],true);
  assert.equal(s.ToolbarGlyphOpacity['Toolbar Copy'],.35);
  assert.ok(s.CommandBindingCount>s.CommandBindingDistinct&&s.CommandBindingDistinct>10);
  await click('Toolbar Copy');await textIs('');
  await click('Toolbar Indent');
  await textIs(s.ConvertTabsToSpaces?' '.repeat(s.TabSize):'\t');
  await wait(()=>globalThis.__editorSceneState?.CommandSurfaceEnabled?.['Toolbar Undo']===true);
  await click('Toolbar Undo');await textIs('');
  checks.push('Native toolbar state disables unavailable clipboard/history actions and permits real indentation in an empty document');
  await paste('alpha');await page.keyboard.press('Control+a');
  await wait(()=>globalThis.__editorSceneState?.CommandSurfaceEnabled?.['Toolbar Copy']&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Cut']);
  assert.equal((await state()).ToolbarGlyphOpacity['Toolbar Copy'],1);
  await click('Toolbar Copy');
  await wait(()=>globalThis.__editorSceneState?.FocusedControl?.endsWith('.TextArea'));
  assert.equal(await page.evaluate(()=>navigator.clipboard.readText()),'alpha');
  await command('View','Read Only');
  await wait(()=>globalThis.__editorSceneState?.ReadOnly&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Cut']===false);
  s=await state();
  for(const name of ['Cut','Paste','Delete','Indent','Format document'])assert.equal(s.CommandSurfaceEnabled['Toolbar '+name],false,name);
  assert.equal(s.CommandSurfaceEnabled['Toolbar Copy'],true);
  const revision=s.Revision;
  await click('Toolbar Cut');await textIs('alpha');assert.equal((await state()).Revision,revision);
  await click('Menu Edit');
  await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.Copy===true&&globalThis.__editorSceneState.VisibleCommandEnabled.Cut===false);
  await click('Visible command Cut');
  assert.ok((await state()).OpenMenuCount>0,'Disabled native menu action must not execute or dismiss');
  await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await click('Toolbar commands');await click('Visible submenu Edit');
  await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===2&&globalThis.__editorSceneState.VisibleCommandEnabled?.Paste===false&&globalThis.__editorSceneState.VisibleCommandEnabled.Copy===true);
  await page.screenshot({path:path.join(output,'command-bindings-readonly.png')});
  // Two immediate Escapes can race a native submenu's closing/focus restoration.
  // Enforce the child transition, including absence of orphaned child commands,
  // before sending the parent's Escape. No repeated keys or retry on failure.
  await page.keyboard.press('Escape');
  await wait(()=>{
    const s=globalThis.__editorSceneState;
    return s?.OpenMenuCount===1&&s.VisibleCommandEnabled?.Copy===undefined&&!!s.Controls?.['Visible submenu Edit'];
  });
  await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await textIs('alpha');assert.equal((await state()).Revision,revision);
  checks.push('Read-only availability agrees across toolbar, main Edit menu and overflow while disabled native commands cannot mutate text');
  checks.push('Escape dismisses the child overflow menu before its parent without leaving orphan commands or changing text');
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  await command('Macros','Start Recording');
  await wait(()=>globalThis.__editorSceneState?.MacroState==='Recording'&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Find']===false);
  await command('Macros','Pause Recording');
  await wait(()=>globalThis.__editorSceneState?.MacroState==='Paused'&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Find']===true);
  await click('Toolbar Find');await wait(()=>globalThis.__editorSceneState?.SearchOpen);
  await click('PART_close');await wait(()=>globalThis.__editorSceneState?.SearchOpen===false);
  await click('Toolbar Find');await wait(()=>globalThis.__editorSceneState?.SearchOpen);
  await command('Macros','Resume Recording');
  await wait(()=>globalThis.__editorSceneState?.MacroState==='Recording'&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Find']===false&&globalThis.__editorSceneState.SearchOpen===false);
  await command('Macros','Cancel Recording');
  await wait(()=>globalThis.__editorSceneState?.MacroState==='Idle'&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Find']===true);
  await textIs('alpha');assert.equal((await state()).MacroStepCount,0,'Paused search interactions must not become recorded steps');
  checks.push('Paused macro recording permits actual search button input and resuming closes interactive search before restoring recording restrictions');
  await click('Language');await click('Choice Plain text');await wait(()=>globalThis.__editorSceneState?.Language==='plaintext');
  assert.equal((await state()).CommandSurfaceEnabled['Toolbar Complete word'],false);
  assert.equal((await state()).CommandSurfaceEnabled['Toolbar Format document'],false);
  await command('Window','Split Editor');
  await wait(()=>globalThis.__editorSceneState?.Split&&globalThis.__editorSceneState.OpenMenuCount===0);
  await click('Secondary editor');
  await wait(()=>globalThis.__editorSceneState?.ActiveView==='Secondary');
  await command('View','Read Only');
  await wait(()=>globalThis.__editorSceneState?.ReadOnly&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Paste']===false&&globalThis.__editorSceneState.OpenMenuCount===0);
  await click('Primary editor');
  await wait(()=>globalThis.__editorSceneState?.ActiveView==='Primary'&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Paste']===true);
  await click('Secondary editor');
  await wait(()=>globalThis.__editorSceneState?.ActiveView==='Secondary'&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Paste']===false);
  await command('Window','Split Editor');await wait(()=>globalThis.__editorSceneState?.Split===false&&globalThis.__editorSceneState.ActiveView==='Primary');
  checks.push('Language capability changes and independent split read-only state update the same bound native command controls');
  await paste('abcdef');await wait(()=>globalThis.__editorSceneState?.AnalysisRevision===globalThis.__editorSceneState.Revision);
  await page.keyboard.press('Control+Home');await page.keyboard.press('ArrowRight');
  await wait(()=>globalThis.__editorSceneState?.CaretOffset===1&&globalThis.__editorSceneState.CommandSurfaceEnabled['Toolbar Delete']);
  const writes=(await state()).CommandBindingWrites;
  for(let i=0;i<8;i++){await page.keyboard.press('ArrowRight');await page.keyboard.press('ArrowLeft');}
  await wait(()=>globalThis.__editorSceneState?.CaretOffset===1);
  assert.equal((await state()).CommandBindingWrites,writes,'Caret events with unchanged command state must not rewrite native properties');
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await page.screenshot({path:path.join(output,'command-bindings-light.png')});
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await page.screenshot({path:path.join(output,'command-bindings-dark.png')});
  await click('Language');await click('Choice C#');await wait(()=>globalThis.__editorSceneState?.Language==='csharp');
  await textIs('abcdef');await focusEditor();
  checks.push('Unchanged caret-driven command updates leave native properties untouched and disabled glyphs remain legible in both themes');
}
