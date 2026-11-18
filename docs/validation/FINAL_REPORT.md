# Validation after the support-hand change and group A - build fd4dec2 (matrix on e1da4aa, identical weapon code)

## Full matrix: 12 long guns x 8 characters x 5 states, fire from 3 sides = 672 readings - 0 failures
| gun | before (497b597) ext / slide / at 20 deg | after ext / slide / at 20 deg |
|---|---|---|
| rifle, rifle_marksman, rifle_storm, shotgun (Benelli), smg, smg_hornet, smg_viper | unchanged | unchanged (0 slide, 0 at the ceiling) |
| shotgun_breacher | 0.97 / 11 / 36 of 56 | 0.96 / 4 / 6 |
| shotgun_long | 0.98 / 12 / 37 | 0.97 / 6 / 9 |
| sniper | 0.97 / 12 / 37 | 0.96 / 5 / 9 |
| sniper_long | 0.97 / 10 / 36 | 0.96 / 4 / 5 |
| sniper_swift | 0.97 / 9 / 24 | 0.96 / 2 / 4 |
Right hand 0.0 cm on its anchor, left palm 0.0 cm on its target, everywhere. Right hand wrapped round the grip: 96 / 96 in
each of fire, crouch-fire, idle, Rifle Run, walk-aim.

## Group A
- Backpack (Lv1-3 x 8 characters, side and back views): on the back of every outfit, none inside the body (the back is
  measured from each outfit's own mesh), straps toward the back (Lv1 / Lv3 turned round). Archer: the small Lv1 pack sits
  behind her quiver.
- Rocks: Highlands summit pieces 0 floating (were 8, up to 1.16 m); boulders bedded at build time on every map.
- Ice wall in a match: the wall stays, cracks appear in stages, the see-through material is found and supported. In the test
  browser (software rendering) the crystal itself draws black, so the cracks are hard to see there - to be checked on device.
