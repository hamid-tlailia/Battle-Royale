/** Original SVG icon set for the touch buttons (stroke = currentColor). */
const svg = (body: string) => `<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;

export const ICONS = {
  fire: svg('<circle cx="24" cy="24" r="11"/><path d="M24 5v9M24 34v9M5 24h9M34 24h9"/><circle cx="24" cy="24" r="2.5" fill="currentColor"/>'),
  jump: svg('<path d="M24 38V12M13 22l11-11 11 11"/><path d="M14 42h20"/>'),
  crouch: svg('<circle cx="27" cy="9" r="4"/><path d="M26 15l-7 9 8 4-3 12"/><path d="M19 24l-6 6M27 28l9 3"/><path d="M12 44h26"/>'),
  prone: svg('<circle cx="39" cy="27" r="4"/><path d="M34 29l-14 2-10 2"/><path d="M24 31l-4 6M16 32l-7 4"/><path d="M6 42h36"/>'),
  scope: svg('<circle cx="24" cy="24" r="15"/><circle cx="24" cy="24" r="6"/><path d="M24 4v8M24 36v8M4 24h8M36 24h8"/>'),
  reload: svg('<path d="M38 20a15 15 0 1 0 1 9"/><path d="M39 9v11H28"/>'),
  sprint: svg('<circle cx="30" cy="8" r="4"/><path d="M22 18l8-4 6 7 6 1"/><path d="M30 14l-5 12 7 6-2 11"/><path d="M25 26l-7 6-8-1"/>'),
  settings: svg('<circle cx="24" cy="24" r="6"/><path d="M24 4v6M24 38v6M4 24h6M38 24h6M10 10l4 4M34 34l4 4M38 10l-4 4M14 34l-4 4"/>'),
} as const;
