import { ICONS } from './icons';

/** Every movable HUD control. Shared by the game and the lobby's layout editor. */
export const BUTTONS = ['fire', 'fire-left', 'jump', 'crouch', 'prone', 'scope', 'reload', 'sprint', 'heal', 'frost', 'bag'] as const;
export type ButtonId = (typeof BUTTONS)[number];

const ICON: Record<ButtonId, string> = {
  fire: ICONS.fire, 'fire-left': ICONS.fire, jump: ICONS.jump, crouch: ICONS.crouch, prone: ICONS.prone, scope: ICONS.scope,
  reload: ICONS.reload, sprint: ICONS.sprint, heal: ICONS.heal + '<b>0</b>', frost: ICONS.frost + '<b>0</b>', bag: ICONS.bag,
};

/** short caption under each button so its job is obvious */
const LABEL: Partial<Record<ButtonId, string>> = {
  fire: 'FIRE', jump: 'JUMP', crouch: 'CROUCH', prone: 'PRONE', scope: 'SCOPE', reload: 'RELOAD',
  sprint: 'RUN', heal: 'HEAL', frost: 'ICE WALL', bag: 'BAG',
};

export function createHudButtons(root: HTMLElement): Record<ButtonId, HTMLDivElement> {
  const out = {} as Record<ButtonId, HTMLDivElement>;
  for (const id of BUTTONS) {
    const b = document.createElement('div');
    b.className = `btn btn-${id}`;
    b.dataset.hud = id;
    b.innerHTML = ICON[id] + (LABEL[id] ? `<small>${LABEL[id]}</small>` : '');
    root.appendChild(b);
    out[id] = b;
  }
  return out;
}
