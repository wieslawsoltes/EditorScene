import assert from 'node:assert/strict';
import path from 'node:path';

// Inspect the actual Uno/Skia screenshot, not browser HTML styles or a model's
// expected palette. The detached canvas below only decodes screenshot pixels;
// it never changes the application, its controls, or the diagnostic snapshot.
export async function checkDocumentTabColors({page,state,theme,output,suffix}) {
  const foreground={Light:32,Dark:212,'High contrast':255}[theme];
  assert.notEqual(foreground,undefined,'Unknown screenshot palette');
  await page.mouse.move(1,650);
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const s=await state();
  const edge=s.Controls['All documents'][0];
  const boxes=Object.entries(s.Controls).filter(([name,b])=>name.startsWith('Document ')
    &&b[0]>=0&&b[0]+b[2]<=edge+1&&b[2]>20&&b[3]>=20);
  assert.ok(boxes.length>=2,'Expected multiple fully visible retained document captions');
  const png=await page.screenshot({path:path.join(output,'document-tabs-'+suffix+'.png')});
  const viewport=page.viewportSize();
  const samples=await page.evaluate(async({png,boxes,foreground,viewport})=>{
    const bytes=Uint8Array.from(atob(png),c=>c.charCodeAt(0));
    const image=await createImageBitmap(new Blob([bytes],{type:'image/png'}));
    try {
      const canvas=new OffscreenCanvas(image.width,image.height);
      const context=canvas.getContext('2d',{willReadFrequently:true});
      context.drawImage(image,0,0);
      const pixels=context.getImageData(0,0,image.width,image.height).data;
      const sx=image.width/viewport.width,sy=image.height/viewport.height;
      return boxes.map(([name,b])=>{
        // Exclude the tab's outline/accent and side padding. Count ink close to
        // the expected caption color, allowing edge antialiasing differences.
        const left=Math.max(0,Math.ceil((b[0]+9)*sx));
        const right=Math.min(image.width,Math.floor((b[0]+b[2]-9)*sx));
        const top=Math.max(0,Math.ceil((b[1]+3)*sy));
        const bottom=Math.min(image.height,Math.floor((b[1]+b[3]-5)*sy));
        let ink=0;
        for(let y=top;y<bottom;y++)for(let x=left;x<right;x++) {
          const p=(y*image.width+x)*4;
          if(pixels[p+3]>200&&Math.abs(pixels[p]-foreground)<=24
            &&Math.abs(pixels[p+1]-foreground)<=24&&Math.abs(pixels[p+2]-foreground)<=24)ink++;
        }
        return {name,ink};
      });
    } finally { image.close(); }
  },{png:png.toString('base64'),boxes,foreground,viewport});
  for(const sample of samples)assert.ok(sample.ink>=4,
    `${theme}: ${sample.name} has no legible expected foreground ink (${sample.ink} pixels)`);
}
