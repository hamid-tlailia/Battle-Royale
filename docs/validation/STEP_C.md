# Phase 2 Step C - WeaponIKController validation (build c2ba904)

## Initialisation (focused tests, accepted)
posethresh=999 / normal / animdelay=30 (gameplay) / 14-clip transitions: no solve before the pose is written, gun hidden until placed, fade 8 frames and >= 0.15 s, right palm 0.0 cm, latched across 685 frames.

## Matrix: 8 characters x 12 long guns x 5 states (idle, walk-aim, sprint, fire, crouch-fire), side view + front/rear for fire
480 combinations. 35 fail (left palm > 1.5 cm off its target with the IK fully on). Right palm on RightHandGrip <= 1.5 cm everywhere.

| failing | states | characters |
|---|---|---|
| shotgun (support grip 0.54 m ahead of the pistol grip) | walk-aim, fire, crouch-fire | all 8 (Spec Ops 1.9-2.9 cm ... Shadow 8.1 cm) |
| shotgun_long (0.51 m) | walk-aim, fire, crouch-fire | 7 (all but Exo / Spec Ops) |
| none of: rifles (0.31-0.33 m), smgs (0.20-0.27 m), shotgun_breacher (0.43 m), snipers (0.40-0.45 m) | | |
idle and sprint: 0 failures (gun held lower and closer).

## Root cause (wikfail readout, 528 lines)
- reach = target-to-left-shoulder / arm length, before the 12 cm slide: 1.17-1.31 for the failing cases.
- **the same before and after the chest's aiming turn** (12-20 deg applied): the turn rotates shoulders, right hand and gun together, so it does not change the reach. Not the turn, not the order of turn and IK.
- not mx_walk specifically: fire and crouch-fire fail the same way.
- cause: the Mixamo rifle stance holds the right hand at the shoulder; a support grip more than ~0.45 m ahead of the pistol grip lies beyond any of the 8 arms (0.56-0.62 m) in that stance.

## Gameplay
- rapid transitions (14 clips): 0 bad frames; 1 failure in crawl (Step F).
- weapon switching while walking / aiming (rifle <-> shotgun <-> pistol): failures only with the shotgun in walk-aim (same cause).
- character switching (30 swaps, 8 characters): 30/30, no IK failure.

## Proposed correction (not applied)
A. shoulder protraction in the IK chain: when the target is out of reach, the support clavicle (LeftShoulder) turns toward it, up to 20 deg - generic, as a person reaching forward.
B. per-weapon stance data (weapon_grips.json, e.g. "holdBack"): the right hand pulled back along the barrel a few cm for long-span guns (the right elbow bends more).
No slide-limit change, no grip-coordinate change, no per-character offset.
