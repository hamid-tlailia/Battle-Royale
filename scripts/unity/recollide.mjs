// Rebuilds the collision shapes of already imported props (Models/<key>.glb) - e.g. after the glass rule changed.
// Run: node scripts/unity/recollide.mjs bld_panel bld_shop ...
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { writeShape } from './import-props.mjs';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
for (const key of process.argv.slice(2)) {
  const doc = await io.read(`unity/Assets/LastWar/Resources/Models/${key}.glb`);
  writeShape(key, doc.getRoot().listScenes()[0]);
}
