import assert from 'node:assert/strict';
import path from 'node:path';

export async function checkLineEndings({page,state,wait,textIs,paste,click,command,focusEditor,output,checks}) {
  // Text changes before a native menu's close/focus continuation completes.
  // Observe keyboard readiness without clicking the editor or moving its caret.
  const formatIs=ending=>wait(expected=>globalThis.__editorSceneState?.StatusLineEndings===expected,ending);
  const convert=async(suffix,text)=>{
    await command('Advanced','Convert Line Endings to '+suffix);
    await wait(expected=>{
      const s=globalThis.__editorSceneState;
      return s?.TextPrefix===expected&&s.OpenMenuCount===0&&s.FocusedControl?.endsWith('.TextArea');
    },text);
    await formatIs(suffix);
  };
  await command('File','New');
  const source='first\r\nsecond\nthird\rfourth🙂';
  const crlf='first\r\nsecond\r\nthird\r\nfourth🙂';
  await paste(source); await page.keyboard.press('Control+Home');
  await wait(()=>globalThis.__editorSceneState?.StatusFormat==='UTF-8  Mixed'&&globalThis.__editorSceneState.CaretOffset===0&&!!globalThis.__editorSceneState.Controls?.['Document format']);
  const formatBuilds=(await state()).StatusFormatRefreshes;
  await page.keyboard.press('ArrowRight');await wait(()=>globalThis.__editorSceneState?.CaretOffset===1);
  await page.keyboard.type('X');await textIs('fXirst\r\nsecond\nthird\rfourth🙂');
  await page.keyboard.press('Control+z');await textIs(source);await formatIs('Mixed');
  assert.equal((await state()).StatusFormatRefreshes,formatBuilds,'Ordinary typing, caret movement and undo must not rebuild unchanged format metadata');
  await page.screenshot({path:path.join(output,'line-ending-metadata-mixed-dark.png')});
  checks.push('Native status reports mixed source accurately and retains formatted text through ordinary typing, caret movement and undo');
  await convert('CRLF',crlf);
  await page.screenshot({path:path.join(output,'line-ending-metadata-crlf-dark.png')});
  await page.keyboard.press('Control+z'); await textIs(source);await formatIs('Mixed');
  await page.keyboard.press('Control+y'); await textIs(crlf);await formatIs('CRLF');
  await page.keyboard.press('Control+Home'); await page.keyboard.press('Shift+ArrowRight');
  const before=await wait(()=>{
    const s=globalThis.__editorSceneState;
    return s?.CaretOffset===1&&s.SelectionStart===0&&s.SelectionLength===1;
  });
  await convert('CRLF',crlf);
  let s=await state();
  for(const key of ['Revision','CaretOffset','SelectionStart','SelectionLength','Modified'])assert.equal(s[key],before[key],key+' changed on canonical conversion');
  await page.keyboard.press('Control+z'); await textIs(source);await formatIs('Mixed');
  checks.push('Native line-ending conversion preserves non-delimiter text and canonical repeat commands do not add an undo unit or move selection');

  await convert('CR','first\rsecond\rthird\rfourth🙂');
  await wait(()=>globalThis.__editorSceneState?.LineCount===4);
  await page.keyboard.press('Control+z');await textIs(source);await formatIs('Mixed');
  await convert('LF','first\nsecond\nthird\nfourth🙂');
  await page.keyboard.press('Control+z');await textIs(source);await formatIs('Mixed');
  checks.push('All three native conversion commands update format status, preserve logical lines and undo mixed source exactly');

  await paste('one\ntwo\n');await page.keyboard.press('Control+z');await textIs(source);
  await page.keyboard.press('Control+y');await textIs('one\ntwo\n');
  await page.keyboard.press('Control+Home');await page.keyboard.type('X');await textIs('Xone\ntwo\n');
  await page.keyboard.press('Control+z');
  const redoBaseline=await wait(()=>{
    const s=globalThis.__editorSceneState;
    return s?.TextPrefix==='one\ntwo\n'&&s.CommandSurfaceEnabled?.['Toolbar Redo']===true;
  });
  await convert('LF','one\ntwo\n');
  assert.equal((await state()).Revision,redoBaseline.Revision);
  await page.keyboard.press('Control+y');await textIs('Xone\ntwo\n');
  checks.push('A canonical LF command preserves the redo branch instead of silently replacing already-correct line breaks');

  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly);
  await click('Menu Advanced');await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Convert Line Endings to CR']===false);
  s=await state();
  for(const suffix of ['CR','LF','CRLF'])assert.equal(s.VisibleCommandEnabled['Convert Line Endings to '+suffix],false);
  await page.screenshot({path:path.join(output,'line-endings-readonly-dark.png')});
  await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await command('View','Read Only');await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
  await click('Theme');await click('Choice Light');await wait(()=>globalThis.__editorSceneState?.Theme==='Light');
  await click('Menu Advanced');await wait(()=>globalThis.__editorSceneState?.VisibleCommandEnabled?.['Convert Line Endings to CR']===true);
  await page.screenshot({path:path.join(output,'line-endings-light.png')});
  await page.keyboard.press('Escape');await wait(()=>globalThis.__editorSceneState?.OpenMenuCount===0);
  await paste('unterminated🙂');await formatIs('None');
  assert.equal((await state()).StatusFormat,'UTF-8  None');
  await page.screenshot({path:path.join(output,'line-ending-metadata-none-light.png')});
  await page.keyboard.press('Control+z');await textIs('Xone\ntwo\n');await formatIs('LF');
  await page.setViewportSize({width:520,height:700});
  await wait(()=>globalThis.__editorSceneState?.Controls?.['Status bar']?.[2]===520&&globalThis.__editorSceneState.Controls['Document format']===undefined);
  assert.equal((await state()).StatusLineEndings,'LF','Hidden compact-format label must keep its correct metadata');
  await page.setViewportSize({width:1280,height:800});
  await wait(()=>globalThis.__editorSceneState?.Controls?.['Status bar']?.[2]===1280&&!!globalThis.__editorSceneState.Controls['Document format']);
  await click('Theme');await click('Choice Dark');await wait(()=>globalThis.__editorSceneState?.Theme==='Dark');
  await focusEditor();await textIs('Xone\ntwo\n');
  checks.push('Native line-ending menu states follow readonly permissions, and exact format status survives themes, unterminated text, undo and compact resizing');
}
