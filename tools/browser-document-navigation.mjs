import assert from 'node:assert/strict';
import path from 'node:path';
import {checkDocumentTabColors} from './browser-tab-colors.mjs';

export async function checkDocumentNavigation({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  const before=await state(); const count=before.DocumentCount; const built=before.DocumentTabBuilds; const menuBuilt=before.DocumentMenuBuilds;
  const names=[];
  for(let i=1;i<=6;i++) {
    await command('File','New');
    await wait(n=>globalThis.__editorSceneState?.DocumentCount===n,count+i);
    names.push((await state()).FileName);
  }
  let s=await state();
  assert.equal(s.DocumentTabBuilds,built+6,'Adding six documents must not recreate old native tabs');
  assert.equal(s.DocumentMenuBuilds,menuBuilt+6,'Adding six documents must build exactly six document-menu items');
  await paste('unsaved navigation text'); await textIs('unsaved navigation text');
  await wait(()=>globalThis.__editorSceneState?.Modified&&globalThis.__editorSceneState.DocumentMenuCheckCount===1);
  await click('All documents');
  await wait(name=>globalThis.__editorSceneState?.VisibleCommandEnabled?.[name+' *']===true,names[5]);
  await click('Visible command '+names[0]);
  await wait(name=>globalThis.__editorSceneState?.FileName===name&&globalThis.__editorSceneState.OpenMenuCount===0,names[0]);
  await textIs('');
  assert.equal((await state()).DocumentTabBuilds,built+6);
  await focusEditor(); await page.keyboard.press('Control+PageDown');
  await wait(name=>globalThis.__editorSceneState?.FileName===name,names[1]);
  await page.keyboard.press('Control+PageUp');
  await wait(name=>globalThis.__editorSceneState?.FileName===name,names[0]);
  await textIs('');
  await command('Window','Previous Document');
  await wait(name=>globalThis.__editorSceneState?.FileName===name,before.FileName);
  await command('Window','Next Document');
  await wait(name=>globalThis.__editorSceneState?.FileName===name,names[0]);
  checks.push('Identity-based native document menu preserves dirty documents and real shortcuts plus reverse/forward menus reuse existing tabs');

  await page.setViewportSize({width:520,height:700});
  await wait(()=>{
    const s=globalThis.__editorSceneState; const button=s?.Controls?.['All documents'];
    return button&&button[0]>=0&&button[0]+button[2]<=521&&s.DocumentTabViewportWidth<500;
  });
  await click('All documents');
  await wait(name=>globalThis.__editorSceneState?.VisibleCommandEnabled?.[name+' *']===true,names[5]);
  await page.screenshot({path:path.join(output,'document-menu-narrow.png')});
  await click('Visible command '+names[5]+' *');
  await wait(name=>{
    const s=globalThis.__editorSceneState; const tab=s?.Controls?.['Document '+name]; const button=s?.Controls?.['All documents'];
    return s?.FileName===name&&tab&&button&&tab[0]>=-1&&tab[0]+tab[2]<=button[0]+1&&s.DocumentTabOffset>0;
  },names[5]);
  await textIs('unsaved navigation text');
  const close=(await state()).Controls['Close document'];
  assert.ok(close[0]>=0&&close[0]+close[2]<=521,'Active close button must remain outside horizontally scrolled tabs');
  checks.push('Narrow document overflow keeps document-list and close controls reachable and scrolls the selected tab into view');

  await page.setViewportSize({width:1280,height:800});
  await wait(()=>globalThis.__editorSceneState?.Controls?.['Status bar']?.[2]===1280);
  await focusEditor(); await page.keyboard.press('Control+Alt+ArrowDown');
  await wait(name=>globalThis.__editorSceneState?.VisibleCommandEnabled?.[name]===true,names[0]);
  await click('Visible command '+names[0]);
  await wait(name=>globalThis.__editorSceneState?.FileName===name&&globalThis.__editorSceneState.OpenMenuCount===0,names[0]);
  for(const [theme,suffix] of [['Light','light'],['Dark','dark'],['High contrast','high-contrast'],['Light','light-roundtrip'],['Dark','dark-roundtrip']]) {
    await click('Theme'); await click('Choice '+theme);
    await wait(expected=>globalThis.__editorSceneState?.Theme===expected&&globalThis.__editorSceneState.OpenMenuCount===0,theme);
    await checkDocumentTabColors({page,state,theme,output,suffix});
    s=await state();
    assert.equal(s.DocumentTabBuilds,built+6,'Palette switches must retain every tab control');
    assert.equal(s.DocumentMenuBuilds,menuBuilt+6,'Palette switches must retain document menu items');
    assert.equal(s.FileName,names[0]);
    await click('All documents'); await wait(()=>globalThis.__editorSceneState?.OpenMenuCount>0);
    assert.equal((await state()).VisibleCommandEnabled[names[5]+' *'],true,'Theme switches must retain the unsaved caption');
    await page.screenshot({path:path.join(output,'document-menu-'+suffix+'.png')});
    await page.keyboard.press('Escape'); await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  }
  checks.push('Actual Skia screenshot ink remains legible in retained tabs through Light, Dark and High Contrast round trips without rebuilding controls');
  s=await state(); assert.equal(s.DocumentMenuCheckCount,1);
  assert.equal(s.DocumentTabBuilds,built+6); assert.equal(s.DocumentMenuBuilds,menuBuilt+6);
  await command('File','Close'); await wait(n=>globalThis.__editorSceneState?.DocumentCount===n,count+5);
  s=await state(); assert.equal(s.DocumentTabBuilds,built+6); assert.equal(s.DocumentMenuBuilds,menuBuilt+6);
  await focusEditor();
  checks.push('Keyboard document list, theme changes and closing a tab retain native control identities without losing unsaved text');
}
