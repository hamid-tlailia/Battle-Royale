import sharp from 'sharp';
import fs from 'fs';
const res = process.argv[2];
const art = (pad) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
<defs><linearGradient id="s" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8fd0ff"/><stop offset="1" stop-color="#d9f1ff"/></linearGradient></defs>
<rect width="512" height="512" fill="url(#s)"/>
<g transform="translate(${pad} ${pad}) scale(${(512 - 2 * pad) / 512})">
<path d="M0 360 L150 170 L230 260 L330 120 L512 340 L512 512 L0 512Z" fill="#3f7a35"/>
<path d="M330 120 L372 172 L346 166 L326 186 L300 160Z" fill="#f2f6f2"/>
<path d="M0 420 Q256 330 512 420 L512 512 L0 512Z" fill="#2c5a26"/>
<text x="256" y="470" font-family="Arial Black, Arial, sans-serif" font-weight="900" font-size="120" fill="#fff" text-anchor="middle" stroke="#13200f" stroke-width="10" paint-order="stroke">LW</text>
</g></svg>`;
const sizes = { mdpi: 48, hdpi: 72, xhdpi: 96, xxhdpi: 144, xxxhdpi: 192 };
for (const [d, s] of Object.entries(sizes)) {
  const dir = `${res}/mipmap-${d}`;
  await sharp(Buffer.from(art(0))).resize(s, s).png().toFile(`${dir}/ic_launcher.png`);
  await sharp(Buffer.from(art(0))).resize(s, s).composite([{ input: Buffer.from(`<svg width="${s}" height="${s}"><circle cx="${s/2}" cy="${s/2}" r="${s/2}" fill="#fff"/></svg>`), blend: 'dest-in' }]).png().toFile(`${dir}/ic_launcher_round.png`);
  const f = Math.round(s * 108 / 48);
  await sharp(Buffer.from(art(80))).resize(f, f).png().toFile(`${dir}/ic_launcher_foreground.png`);
}
fs.writeFileSync(process.argv[3], await sharp(Buffer.from(art(0))).resize(192, 192).png().toBuffer());
