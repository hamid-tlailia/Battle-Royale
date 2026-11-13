// Original "LAST WAR" logo -> client/public/logo.png (+ Android splash screens).
import sharp from 'sharp';
import fs from 'node:fs';
const W = 1200, H = 620;
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
<defs>
  <linearGradient id="gold" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff6c8"/><stop offset=".45" stop-color="#ffd34d"/><stop offset=".55" stop-color="#e0a21c"/><stop offset="1" stop-color="#8a5a0c"/></linearGradient>
  <linearGradient id="hill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#9bd96a"/><stop offset="1" stop-color="#2c5a26"/></linearGradient>
  <radialGradient id="glow" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#ffd34d" stop-opacity=".35"/><stop offset="1" stop-color="#ffd34d" stop-opacity="0"/></radialGradient>
  <clipPath id="ring"><circle cx="600" cy="230" r="170"/></clipPath>
</defs>
<circle cx="600" cy="230" r="260" fill="url(#glow)"/>
<g clip-path="url(#ring)">
  <rect x="400" y="40" width="400" height="400" fill="#16301a"/>
  <path d="M400 330 L500 210 L560 270 L640 150 L800 320 L800 420 L400 420Z" fill="url(#hill)"/>
  <path d="M640 150 L672 186 L652 182 L636 198 L616 178Z" fill="#f2f6f2"/>
  <path d="M400 360 Q600 300 800 370 L800 420 L400 420Z" fill="#1d3f1c"/>
</g>
<circle cx="600" cy="230" r="170" fill="none" stroke="url(#gold)" stroke-width="16"/>
<circle cx="600" cy="230" r="140" fill="none" stroke="#ffd34d" stroke-opacity=".5" stroke-width="3"/>
<g stroke="url(#gold)" stroke-width="14" stroke-linecap="round">
  <path d="M600 30v70M600 360v28M400 230h70M730 230h70"/>
</g>
<text x="600" y="530" text-anchor="middle" font-family="Inter, Inter Display, DejaVu Sans" font-weight="900" font-size="168" letter-spacing="14"
  fill="url(#gold)" stroke="#2a1a04" stroke-width="10" paint-order="stroke">LAST WAR</text>
<text x="600" y="596" text-anchor="middle" font-family="Inter, DejaVu Sans" font-weight="800" font-size="40" letter-spacing="22" fill="#cfe8b8">BATTLE ROYALE</text>
</svg>`;
fs.writeFileSync('client/public/logo.svg', svg);
await sharp(Buffer.from(svg)).png().toFile('client/public/logo.png');
// Android splash: logo centred on the game's dark green
const res = 'client/android/app/src/main/res';
const sizes = { 'land-mdpi': [480, 320], 'land-hdpi': [800, 480], 'land-xhdpi': [1280, 720], 'land-xxhdpi': [1600, 960], 'land-xxxhdpi': [1920, 1280],
  'port-mdpi': [320, 480], 'port-hdpi': [480, 800], 'port-xhdpi': [720, 1280], 'port-xxhdpi': [960, 1600], 'port-xxxhdpi': [1280, 1920] };
for (const [k, [w, h]] of Object.entries(sizes)) {
  const lw = Math.round(Math.min(w * 0.6, h * 0.7 * W / H));
  const logo = await sharp(Buffer.from(svg)).resize(lw).png().toBuffer();
  await sharp({ create: { width: w, height: h, channels: 3, background: '#122010' } })
    .composite([{ input: logo, gravity: 'center' }]).png().toFile(`${res}/drawable-${k}/splash.png`);
}
await sharp({ create: { width: 1280, height: 720, channels: 3, background: '#122010' } })
  .composite([{ input: await sharp(Buffer.from(svg)).resize(760).png().toBuffer(), gravity: 'center' }]).png().toFile(`${res}/drawable/splash.png`);
console.log('logo + splash written');
