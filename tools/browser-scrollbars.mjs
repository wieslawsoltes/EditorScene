import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import path from 'node:path';

// Real native input and raster observations, not DOM replacement scrollbars.
// The application bridge remains read-only. Test each scale in a fresh app.
export async function checkScrollBars({browser, origin, output, checks, log}) {
  const fixture = Array.from({length:180}, (_, i) => `${String(i + 1).padStart(3, '0')} ${'0123456789 '.repeat(40)}`).join('\n');
  for (const scale of [1, 2]) {
    const context = await browser.newContext({viewport:{width:1280,height:800},deviceScaleFactor:scale});
    await context.grantPermissions(['clipboard-read','clipboard-write'], {origin});
    const page = await context.newPage();
    const errors = [], responses = [];
    page.on('pageerror', error => errors.push(error.stack ?? error.message));
    page.on('response', response => { if (response.status() >= 400) responses.push(`${response.status()} ${response.url()}`); });
    page.on('console', message => { if (message.type() === 'error') log.push(`scrollbars ${scale}x: ${message.text()}`); });
    const state = () => page.evaluate(() => globalThis.__editorSceneState);
    const wait = async (predicate, argument) => {
      await page.waitForFunction(predicate, argument, {timeout:60000});
      const s = await state(); assert.equal(s.Error, null, s.Error ?? 'Application error'); return s;
    };
    const center = b => [b[0] + b[2] / 2, b[1] + b[3] / 2];
    const stable = async name => {
      // Distinct publications are necessary: cached diagnostics are not proof
      // that a native measure/arrange pass and its animation have settled.
      await page.evaluate(() => { globalThis.__scrollBarProbe = {state:null,key:'',count:0,since:0}; });
      await wait(n => {
        const s = globalThis.__editorSceneState, p = globalThis.__scrollBarProbe;
        if (!s || p.state === s) return false;
        p.state = s;
        const b = s.Controls[n];
        if (!b || b[2] <= 0 || b[3] <= 0) return false;
        const key = JSON.stringify(b);
        if (p.key !== key) { p.key = key; p.count = 1; p.since = performance.now(); return false; }
        return ++p.count >= 3 && performance.now() - p.since >= 200;
      }, name);
      return (await state()).Controls[name];
    };
    const click = async name => { await page.mouse.click(...center(await stable(name))); };
    const command = async (menu, label) => { await click(`Menu ${menu}`); await click(`Command ${label}`); };
    const open = async (name, text) => {
      const chooser = page.waitForEvent('filechooser'); await click('Toolbar Open');
      await (await chooser).setFiles({name,mimeType:'text/plain',buffer:Buffer.from(text)});
      await wait(({name,length}) => { const s=globalThis.__editorSceneState; return s?.FileName===name && s.TextLength===length; }, {name,length:text.length});
    };
    const home = async () => {
      const b = await stable('Editor'); await page.mouse.click(b[0] + 100, b[1] + 10);
      await page.keyboard.press('Control+Home');
      await wait(() => {
        const bars=globalThis.__editorSceneState?.EditorScrollBars;
        return ['Primary.Vertical','Primary.Horizontal'].every(k => bars?.[k]?.Value===0 && bars[k].Offset===0);
      });
    };
    const part = (bar, suffix) => {
      const p = bar.Parts.find(p => p.Visible && p.Name.endsWith(suffix));
      assert.ok(p, `Visible native ${suffix} missing: ${JSON.stringify(bar)}`);
      assert.ok(p.Opacity > .98, `Native ${suffix} must not be transparent: ${JSON.stringify(p)}`);
      return p;
    };
    const thumb = bar => {
      const p = bar.Parts.find(p => p.Visible && p.Kind==='Thumb');
      assert.ok(p, `Native thumb missing: ${JSON.stringify(bar)}`);
      assert.ok(p.Opacity > .98, `Native thumb must remain painted after pointer exit: ${JSON.stringify(p)}`);
      return p;
    };
    const painted = async keys => wait(keys => keys.every(k => {
      const b=globalThis.__editorSceneState?.EditorScrollBars?.[k];
      return b && b.Maximum>100 && b.Parts.some(p=>p.Kind==='Thumb' && p.Visible && p.Opacity>.98);
    }), keys);
    const synced = async key => wait(k => {
      const b = globalThis.__editorSceneState?.EditorScrollBars?.[k];
      return b && Math.abs(b.Value-b.Offset)<.1;
    }, key);
    const checkGeometry = (bar, axis) => {
      assert.equal(bar.IndicatorMode, 'MouseIndicator');
      assert.equal(bar.Name, `Editor ${axis.toLowerCase()} scrollbar`);
      const b=bar.Bounds, v=bar.ViewBounds, vertical=axis==='Vertical';
      assert.ok(b.every(Number.isFinite) && b[2]>0 && b[3]>0);
      assert.ok(b[0]>=0 && b[1]>=0 && b[0]+b[2]<=page.viewportSize().width+.5 && b[1]+b[3]<=page.viewportSize().height+.5, 'Rail must remain inside the window');
      assert.ok(vertical ? b[0]>=v[0]+v[2]-.5 : b[1]>=v[1]+v[3]-.5, 'Rail must not cover the text viewport');
      assert.ok(Math.abs(bar.ViewportSize-v[vertical?3:2])<.5);
      assert.ok(Math.abs(bar.Maximum-Math.max(0,bar.Extent-bar.ViewportSize))<.5);
      assert.ok(Math.abs(bar.Value-bar.Offset)<.1);
    };
    const hoverRail = async key => {
      // Native dynamic templates contract arrows/track after pointer exit while
      // retaining the thumb. Enter that painted thumb, then wait for the real
      // hover animation before requiring the arrow or paging button to be opaque.
      await page.mouse.move(...center(thumb((await state()).EditorScrollBars[key]).Bounds));
      await wait(k => globalThis.__editorSceneState?.EditorScrollBars?.[k]?.Parts.some(
        p => p.Visible && p.Name.endsWith('SmallIncrease') && p.Opacity>.98), key);
      return (await state()).EditorScrollBars[key];
    };
    const screenshotRail = async bar => {
      // Exclude shared cell borders; all sampled pixels belong to the native
      // scrollbar, never text/caret pixels that happen to change when scrolling.
      const b=bar.Bounds, x=Math.ceil(b[0])+1, y=Math.ceil(b[1])+1;
      return page.screenshot({clip:{x,y,width:Math.floor(b[0]+b[2])-x-1,height:Math.floor(b[1]+b[3])-y-1}});
    };
    const drag = async (key, fraction=.65) => {
      const b=(await state()).EditorScrollBars[key], t=thumb(b), start=center(t.Bounds), vertical=key.endsWith('Vertical');
      const end=[...start];
      end[vertical?1:0]=b.Bounds[vertical?1:0]+b.Bounds[vertical?3:2]*fraction;
      await page.mouse.move(...start); await page.mouse.down(); await page.mouse.move(...end,{steps:15}); await page.mouse.up();
      await wait(({key,previous}) => globalThis.__editorSceneState?.EditorScrollBars?.[key]?.Value>previous+1,{key,previous:b.Value});
      await synced(key);
    };
    try {
      const response=await page.goto(`${origin}/EditorScene/?test=1`,{waitUntil:'domcontentloaded'}); assert.equal(response.status(),200);
      await page.waitForFunction(() => globalThis.__editorSceneState?.Ready,null,{timeout:180000});
      await open(`scrollbars-${scale}x.txt`,fixture);
      for (const theme of ['Light','Dark']) {
        if ((await state()).Theme!==theme) { await click('Theme'); await click(`Choice ${theme}`); await wait(t=>globalThis.__editorSceneState?.Theme===t,theme); }
        await home();
        await painted(['Primary.Vertical','Primary.Horizontal']);
        for (const axis of ['Horizontal','Vertical']) {
          await home();
          const key=`Primary.${axis}`;
          let s=await state(), bar=s.EditorScrollBars[key];
          checkGeometry(bar,axis); thumb(bar);
          const revision=s.Revision, caret=s.CaretOffset;
          await page.mouse.move(2,2); await page.waitForTimeout(3000);
          const before=await screenshotRail((await state()).EditorScrollBars[key]);
          // Exercise the existing native arrow-button -> ValueChanged -> viewport path.
          bar=await hoverRail(key);
          await page.mouse.click(...center(part(bar,'SmallIncrease').Bounds));
          await wait(k=>globalThis.__editorSceneState?.EditorScrollBars?.[k]?.Value>0,key); await synced(key);
          bar=(await state()).EditorScrollBars[key];
          assert.ok(Math.abs(bar.Value-bar.SmallChange)<.5, 'Arrow click moves exactly one native small step');
          await drag(key);
          await page.mouse.move(2,2); await page.waitForTimeout(3000);
          bar=(await state()).EditorScrollBars[key]; checkGeometry(bar,axis); thumb(bar);
          const after=await screenshotRail(bar);
          assert.ok(!before.equals(after), 'Moving the thumb must change actual scrollbar raster pixels, not merely its numeric Value');
          await writeFile(path.join(output,`scrollbar-${axis.toLowerCase()}-${theme.toLowerCase()}-${scale}x-start.png`),before);
          await writeFile(path.join(output,`scrollbar-${axis.toLowerCase()}-${theme.toLowerCase()}-${scale}x-scrolled.png`),after);
          const previous=bar.Value, step=Math.min(previous,bar.LargeChange);
          bar=await hoverRail(key);
          await page.mouse.click(...center(part(bar,'LargeDecrease').Bounds));
          await wait(({key,previous})=>globalThis.__editorSceneState?.EditorScrollBars?.[key]?.Value<previous,{key,previous}); await synced(key);
          bar=(await state()).EditorScrollBars[key];
          assert.ok(Math.abs(bar.Value-(previous-step))<1, 'Track click moves exactly one native page');
          s=await state(); assert.equal(s.Revision,revision); assert.equal(s.CaretOffset,caret);
          checks.push(`Painted ${axis.toLowerCase()} native scrollbar survives idle/pointer exit, arrow, thumb drag and track paging / ${theme} / ${scale}x`);
        }
        await page.mouse.move(2,2); await page.waitForTimeout(3000);
        await page.screenshot({path:path.join(output,`scrollbars-${theme.toLowerCase()}-${scale}x.png`)});
      }
      await home();
      let revision=(await state()).Revision;
      const view=await stable('Editor'); await page.mouse.move(...center(view)); await page.mouse.wheel(0,360);
      await wait(()=>globalThis.__editorSceneState?.EditorScrollBars?.['Primary.Vertical']?.Value>0); await synced('Primary.Vertical');
      assert.equal((await state()).Revision,revision);
      checks.push(`Mouse wheel synchronizes the persistent native vertical thumb without editing / ${scale}x`);
      await command('View','Word Wrap'); await wait(()=>globalThis.__editorSceneState?.WordWrap && globalThis.__editorSceneState.EditorScrollBars['Primary.Horizontal'].Enabled===false);
      await wait(()=>globalThis.__editorSceneState?.EditorScrollBars?.['Primary.Horizontal']?.Offset===0);
      assert.equal((await state()).EditorScrollBars['Primary.Horizontal'].IndicatorMode,'MouseIndicator');
      await command('View','Word Wrap'); await wait(()=>globalThis.__editorSceneState?.WordWrap===false && globalThis.__editorSceneState.EditorScrollBars['Primary.Horizontal'].Enabled);
      await home(); await painted(['Primary.Horizontal']); thumb((await state()).EditorScrollBars['Primary.Horizontal']);
      await command('View','Read Only'); await wait(()=>globalThis.__editorSceneState?.ReadOnly);
      await drag('Primary.Vertical'); assert.equal((await state()).Revision,revision);
      await command('View','Read Only'); await wait(()=>globalThis.__editorSceneState?.ReadOnly===false);
      checks.push(`Wrapping disables horizontal scrolling and restores its thumb; read-only documents still scroll / ${scale}x`);
      await home(); await click('Toolbar Split editor');
      await wait(()=>globalThis.__editorSceneState?.Split && Object.keys(globalThis.__editorSceneState.EditorScrollBars).length===4);
      await painted(['Primary.Vertical','Primary.Horizontal','Secondary.Vertical','Secondary.Horizontal']);
      let s=await state();
      for (const [key,bar] of Object.entries(s.EditorScrollBars)) { checkGeometry(bar,key.split('.')[1]); thumb(bar); }
      const other=s.EditorScrollBars['Secondary.Vertical'].Value;
      await drag('Primary.Vertical'); assert.equal((await state()).EditorScrollBars['Secondary.Vertical'].Value,other);
      await click('Toolbar Split editor'); await wait(()=>globalThis.__editorSceneState?.Split===false && Object.keys(globalThis.__editorSceneState.EditorScrollBars).length===2);
      checks.push(`Shared-document split views own painted, independent native scrollbars and close cleanly / ${scale}x`);
      await page.setViewportSize({width:640,height:560});
      await wait(()=>globalThis.__editorSceneState?.Controls?.Editor?.[2]<640);
      await home(); await page.mouse.move(2,2); await page.waitForTimeout(3000);
      for (const [key,bar] of Object.entries((await state()).EditorScrollBars)) { checkGeometry(bar,key.split('.')[1]); thumb(bar); }
      await page.screenshot({path:path.join(output,`scrollbars-narrow-${scale}x.png`)});
      await page.setViewportSize({width:1280,height:800}); await wait(()=>globalThis.__editorSceneState?.Controls?.Editor?.[2]>640);
      await open(`short-${scale}x.txt`,'short');
      await wait(()=>Object.values(globalThis.__editorSceneState?.EditorScrollBars??{}).length===2 && Object.values(globalThis.__editorSceneState.EditorScrollBars).every(b=>b.Maximum===0 && b.Value===0 && b.Offset===0));
      for (const bar of Object.values((await state()).EditorScrollBars)) assert.equal(bar.IndicatorMode,'MouseIndicator');
      await open(`restored-${scale}x.txt`,fixture); await home();
      await painted(['Primary.Vertical','Primary.Horizontal']);
      for (const bar of Object.values((await state()).EditorScrollBars)) thumb(bar);
      checks.push(`Native rails remain contained after resizing and clamp/recover after document replacement / ${scale}x`);
      assert.deepEqual(errors,[],'Scrollbar browser contexts must not throw'); assert.deepEqual(responses,[],'Scrollbar browser assets must load');
    } catch (error) {
      await writeFile(path.join(output,`scrollbars-failure-${scale}x.json`),JSON.stringify(await state().catch(()=>null),null,2));
      await page.screenshot({path:path.join(output,`scrollbars-failure-${scale}x.png`)}).catch(()=>{});
      throw error;
    } finally { await context.close(); }
  }
}
