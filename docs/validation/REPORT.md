# Character validation (build 2e0bbee)

Test bench: WebGL lobby with `?grid=mil|q|mixq&from=N&n=2` (rows = outfits, columns = IDLE / RUN / JUMP / CROUCH / AIM / SHOOT)
and `?swap=1` (30 random outfit changes at runtime). Groups are strictly separated in code (`SoldierBody.Group`).

## Decisions
- Military outfits are worn whole only (no piece mixing). Piece tabs list only the Quaternius characters.
- Pieces mix only inside the Quaternius group (one shared rig).

## Results
| Test | Result |
|---|---|
| Military, 11 outfits individually | PASS - no neck / waist / ankle gaps, shoulders and elbows hold |
| Quaternius, 11 characters individually | PASS after the rig fix (the feet were stretched: IK rig) |
| Quaternius, mixed pieces | PASS - one head / torso / legs / feet each, no gaps, no double hats |
| Runtime outfit switching (30 swaps, both groups) | PASS 30/30 - animator running, gun at the hand, the right pieces only, groups never mixed |
| Gun hold | right hand closed on the pistol grip, left hand under the handguard, fingers wrapped (procedural, both rigs) |

## Fixed in this round
- Hats overlapping: a Quaternius piece with several materials is split into sub-objects (`body_head_1`...) that were never hidden.
- Quaternius rig (on the duplicated outfit_q_*.glb only, original FBX untouched): feet under the shins, legs under the hips.
- Quaternius fingers: their first bones all start in the palm; the knuckles are found per rig.
- Hand grip: fingers curled on the grip, support hand on the handguard, the forearm takes half the wrist twist.
