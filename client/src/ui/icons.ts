/** Original SVG icon set for the touch buttons (stroke = currentColor). */
const svg = (body: string) => `<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;

export const ICONS = {
  // a cartridge with a muzzle flash: "shoot"
  fire: svg('<path d="M20 40V20q0-9 6-13 6 4 6 13v20z"/><path d="M18 40h16"/><path d="M26 22v10" stroke-width="2"/><path d="M9 14l4 3M8 24h5M9 34l4-3M43 14l-4 3M44 24h-5M43 34l-4-3"/>'),
  jump: svg('<path d="M24 36V10M13 21l11-11 11 11"/><path d="M12 43h24"/>'),
  // kneeling soldier
  crouch: svg('<circle cx="25" cy="8" r="4.2"/><path d="M24 14l-3 12h9l1 13"/><path d="M21 26l-9 5 1 8"/><path d="M23 17l9 5 7-2"/><path d="M8 43h32"/>'),
  // soldier lying flat
  prone: svg('<circle cx="40" cy="30" r="4.2"/><path d="M35 33H17l-11 3"/><path d="M33 33l3-7 7-1"/><path d="M4 41h40"/>'),
  // rifle scope seen from the side
  scope: svg('<path d="M6 21h6l4-4h16l4 4h6v8h-6l-4 4H16l-4-4H6z"/><circle cx="24" cy="25" r="3"/><path d="M20 37v6M28 37v6"/>'),
  reload: svg('<path d="M38 20a15 15 0 1 0 1 9"/><path d="M39 9v11H28"/><path d="M22 18h4v12h-4z" stroke-width="2.4"/>'),
  sprint: svg('<circle cx="30" cy="8" r="4"/><path d="M22 18l8-4 6 7 6 1"/><path d="M30 14l-5 12 7 6-2 11"/><path d="M25 26l-7 6-8-1"/>'),
  heal: svg('<rect x="9" y="13" width="30" height="24" rx="4"/><path d="M24 18v14M17 25h14"/><path d="M18 13V9h12v4"/>'),
  frost: svg('<path d="M24 5v38M8 14l32 20M40 14L8 34"/><path d="M19 8l5 5 5-5M19 40l5-5 5 5"/>'),
  // pistol going down into a holster
  holster: svg('<path d="M8 12h24v6H18l-2 9h-6l2-9H8z"/><path d="M30 26v14M24 34l6 6 6-6"/>'),
  bag: svg('<path d="M14 16h20a4 4 0 0 1 4 4v18a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4V20a4 4 0 0 1 4-4z"/><path d="M18 16v-4a6 6 0 0 1 12 0v4"/><path d="M16 27h16"/>'),
  settings: svg('<circle cx="24" cy="24" r="6"/><path d="M24 4v6M24 38v6M4 24h6M38 24h6M10 10l4 4M34 34l4 4M38 10l-4 4M14 34l-4 4"/>'),
} as const;
