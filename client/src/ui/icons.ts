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
  // backpack
  bag: fill('<path d="M18 7a6 6 0 0 1 12 0v3h2a7 7 0 0 1 7 7v22a4 4 0 0 1-4 4H13a4 4 0 0 1-4-4V17a7 7 0 0 1 7-7h2zm3 0v3h6V7a3 3 0 0 0-6 0zM15 27v8h18v-8z" fill-rule="evenodd"/>'),
  settings: svg('<circle cx="24" cy="24" r="6"/><path d="M24 4v6M24 38v6M4 24h6M38 24h6M10 10l4 4M34 34l4 4M38 10l-4 4M14 34l-4 4"/>'),
};

export const ICONS = Object.fromEntries(Object.entries(RAW).map(([k, v]) => [k, wrap(v)])) as Record<keyof typeof RAW, string>;
