// Downloads source GLBs + license metadata from Sketchfab into assets-src/ and writes CREDITS.md.
import fs from 'node:fs';
import path from 'node:path';
import { SOURCES, OUTFITS } from './sources.mjs';

const OUT = 'assets-src';
fs.mkdirSync(OUT, { recursive: true });
const headers = process.env.SKETCHFAB_TOKEN ? { Authorization: `Token ${process.env.SKETCHFAB_TOKEN}` } : {};
const api = async (p) => (await fetch(`https://api.sketchfab.com/v3${p}`, { headers })).json();

const credits = [];
for (const [key, src] of Object.entries({ ...SOURCES, ...OUTFITS })) {
  const meta = await api(`/models/${src.uid}`);
  credits.push({ key, ...src, name: meta.name, author: meta.user?.displayName || meta.user?.username, profile: meta.user?.profileUrl,
    license: meta.license?.label, licenseUrl: meta.license?.url, url: meta.viewerUrl });
  const file = path.join(OUT, `${key}.glb`);
  if (fs.existsSync(file)) { console.log('cached', key); continue; }
  const dl = await api(`/models/${src.uid}/download`);
  if (!dl.glb?.url) throw new Error(`No GLB download for ${key}: ${JSON.stringify(dl).slice(0, 200)}`);
  const buf = Buffer.from(await (await fetch(dl.glb.url)).arrayBuffer());
  fs.writeFileSync(file, buf);
  console.log('downloaded', key, (buf.length / 1e6).toFixed(1), 'MB');
}
fs.writeFileSync(path.join(OUT, 'credits.json'), JSON.stringify(credits, null, 2));

const lines = [
  '# Credits', '',
  'Last War uses the following third-party 3D assets. All are used under their stated licenses;',
  'they were converted (rescaled, re-textured at lower resolution, compressed, split, animations retargeted).', '',
  '| Asset | Used for | Author | License | Source |', '|---|---|---|---|---|',
  ...credits.map((c) => `| ${c.name} | ${c.use} | [${c.author}](${c.profile}) | [${c.license}](${c.licenseUrl}) | ${c.url} |`),
  '', fs.readFileSync('scripts/assets/sounds-credits.md', 'utf8'),
  '', 'Game code, map generation, terrain and UI are original work.', '',
];
fs.writeFileSync('CREDITS.md', lines.join('\n'));
console.log('CREDITS.md written');
