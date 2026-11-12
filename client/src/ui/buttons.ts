import { ICONS } from './icons';

/** Every movable HUD control. Shared by the game and the lobby's layout editor. */
export const BUTTONS = ['fire', 'fire-left', 'jump', 'crouch', 'prone', 'scope', 'reload', 'sprint', 'heal', 'frost', 'holster', 'bag'] as const;
export type ButtonId = (typeof BUTTONS)[number];

const ICON: Record<ButtonId, string> = {
  fire: ICONS.fire, 'fire-left': ICONS.fire, jump: ICONS.jump, crouch: ICONS.crouch, prone: ICONS.prone, scope: ICONS.scope,
  reload: ICONS.reload, sprint: ICONS.sprint, heal: ICONS.heal + '<b>0</b>', frost: ICONS.frost + '<b>0</b>', holster: ICONS.holster, bag: ICONS.bag,
};

export function createHudButtons(root: HTMLElement): Record<ButtonId, HTMLDivElement> {
  const out = {} as Record<ButtonId, HTMLDivElement>;
  for (const id of BUTTONS) {
    const b = document.createElement('div');
    b.className = `btn btn-${id}`;
    b.dataset.hud = id;
    b.innerHTML = ICON[id];
    root.appendChild(b);
    out[id] = b;
  }
  return out;
}
