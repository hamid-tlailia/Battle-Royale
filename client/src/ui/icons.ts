/** Original SVG icon set for the touch buttons (stroke = currentColor). */
/** filled shapes (white silhouettes) */
const fill = (body: string) => `<g fill="currentColor" stroke="none">${body}</g>`;
const svg = (body: string) => `<g fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round">${body}</g>`;
const wrap = (g: string) => `<svg viewBox="0 0 48 48">${g}</svg>`;

const RAW = {
  // filled cartridge with muzzle sparks: "shoot"
  fire: fill('<path d="M19 41V21q0-10 5-15 5 5 5 15v20z"/><rect x="17" y="40" width="14" height="4" rx="1"/>') + svg('<path d="M9 13l5 4M7 24h6M9 35l5-4M39 13l-5 4M41 24h-6M39 35l-5-4" stroke-width="3"/>'),
  // solid up arrow over the ground line
  jump: fill('<path d="M24 5l15 16h-9v15H18V21H9z"/><rect x="10" y="40" width="28" height="4" rx="2"/>'),
  // kneeling soldier with rifle
  crouch: fill('<circle cx="22" cy="8" r="4.5"/>') + svg('<path d="M22 14l1 13h9l1 13M23 27l-9 3 1 9M22 17l10 4 9-1" stroke-width="5.5"/><path d="M28 19l14-3" stroke-width="3"/><path d="M6 44h36" stroke-width="3"/>'),
  // soldier lying flat with rifle
  prone: fill('<circle cx="37" cy="27" r="4.5"/>') + svg('<path d="M32 31H16L5 33M30 31l4-6h9" stroke-width="5.5"/><path d="M38 23l8-2" stroke-width="3"/><path d="M3 40h42" stroke-width="3"/>'),
  // aim-down-sights: corner brackets + reticle
  scope: svg('<path d="M6 15V6h9M33 6h9v9M42 33v9h-9M15 42H6v-9" stroke-width="4"/><circle cx="24" cy="24" r="9" stroke-width="3.5"/>') + fill('<circle cx="24" cy="24" r="3"/>'),
  // magazine + circular arrow
  reload: svg('<path d="M39 22a15 15 0 1 0 0 6" stroke-width="4"/>') + fill('<path d="M44 14v11H33z"/><path d="M20 15h8l-1 18h-6z"/>'),
  // running figure
  sprint: fill('<circle cx="30" cy="7" r="4.5"/>') + svg('<path d="M28 14l-5 13 7 6-2 11M23 27l-8 6-7-1M26 16l-8 4-3 7M28 15l7 6 7 0" stroke-width="5.5"/>'),
  // medkit
  heal: fill('<path d="M18 9h12v5h8a4 4 0 0 1 4 4v20a4 4 0 0 1-4 4H10a4 4 0 0 1-4-4V18a4 4 0 0 1 4-4h8zM21 12v2h6v-2zM21 20v6h-6v6h6v6h6v-6h6v-6h-6v-6z" fill-rule="evenodd"/>'),
  // throwable frost grenade (snowflake on the body)
  frost: fill('<path d="M19 6h10v5h-10z"/><path d="M28 7l9-3 2 4-9 3z"/><path d="M24 12c9 0 15 7 15 16s-6 16-15 16-15-7-15-16 6-16 15-16zM24 19v18M16 23.5l16 9M32 23.5l-16 9" fill-rule="evenodd" stroke="#000" stroke-opacity=".55" stroke-width="2.6" stroke-linecap="round"/>'),
  // pistol going down
  holster: fill('<path d="M6 10h28v8H20l-3 12H9l3-12H6z"/>') + svg('<path d="M34 24v16M27 33l7 7 7-7" stroke-width="4"/>'),
  // armor vest
  vest: fill('<path d="M15 5h6l3 5 3-5h6l6 9-3 4v24H12V18l-3-4z"/>') + svg('<path d="M17 26h14M17 32h14" stroke="#000" stroke-opacity=".45" stroke-width="2.4"/>'),
  // clenched fist (empty hands)
  fist: fill('<path d="M14 18a4 4 0 0 1 8 0v-2a4 4 0 0 1 8 0v1a4 4 0 0 1 8 1v12c0 7-5 12-12 12h-2c-6 0-10-4-10-10v-6a4 4 0 0 1 0-8z"/>') + svg('<path d="M14 26h20M22 18v7M30 17v8" stroke="#000" stroke-opacity=".45" stroke-width="2.2"/>'),
  // t-shirt (outfits)
  outfit: fill('<path d="M17 6l-11 6 4 9 5-2v23h18V19l5 2 4-9-11-6c-1 3-4 5-7 5s-6-2-7-5z"/>'),
  // folded map with a pin
  map: fill('<path d="M4 10l12-4 16 5 12-4v31l-12 4-16-5-12 4z M17 9v27 M31 13v27" fill-rule="evenodd"/>') + svg('<path d="M17 9v27M31 13v27" stroke="#000" stroke-opacity=".5" stroke-width="2.4"/>'),
  // two people
  friends: fill('<circle cx="17" cy="15" r="7"/><circle cx="33" cy="17" r="6"/><path d="M3 40c0-8 6-13 14-13s14 5 14 13z"/><path d="M29 40c0-5-2-9-5-11 2-1 6-2 9-2 7 0 12 4 12 13z"/>'),
  // backpack
  bag: fill('<path d="M18 7a6 6 0 0 1 12 0v3h2a7 7 0 0 1 7 7v22a4 4 0 0 1-4 4H13a4 4 0 0 1-4-4V17a7 7 0 0 1 7-7h2zm3 0v3h6V7a3 3 0 0 0-6 0zM15 27v8h18v-8z" fill-rule="evenodd"/>'),
  settings: fill('<path fill-rule="evenodd" d="M39.06 20.32 L44.82 21.28 L44.82 26.72 L39.06 27.68 L37.25 32.04 L40.65 36.80 L36.80 40.65 L32.04 37.25 L27.68 39.06 L26.72 44.82 L21.28 44.82 L20.32 39.06 L15.96 37.25 L11.20 40.65 L7.35 36.80 L10.75 32.04 L8.94 27.68 L3.18 26.72 L3.18 21.28 L8.94 20.32 L10.75 15.96 L7.35 11.20 L11.20 7.35 L15.96 10.75 L20.32 8.94 L21.28 3.18 L26.72 3.18 L27.68 8.94 L32.04 10.75 L36.80 7.35 L40.65 11.20 L37.25 15.96Z M24 17a7 7 0 1 0 0.01 0Z"/>'),
};

export const ICONS = Object.fromEntries(Object.entries(RAW).map(([k, v]) => [k, wrap(v)])) as Record<keyof typeof RAW, string>;
