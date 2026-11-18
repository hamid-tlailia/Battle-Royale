# Sensitive weapons review (breacher, long, sniper, sniper_long, sniper_swift) - build 0f061b8

Only the LeftHandGrip point is moved, back along the barrel (test switch ?lhgz=look:m; weapon_grips.json untouched).
RightHandGrip, gunInHand, GunFrame, the IK and the measuring limits are unchanged. Shotguns + snipers matrix:
8 characters x 5 states (fire from 3 sides) = 56 readings per gun; 0 failures in every variant.

| gun | now: ext / slide max / avg / at 20 deg | back 3 cm | back 6 cm |
|---|---|---|---|
| shotgun_breacher | 0.97 / 11 / 2.8 / 36 of 56 | 0.97 / 7 / 1.0 / 18 | 0.96 / 4 / 0.3 / 6 |
| shotgun_long (critical) | 0.98 / 12 / 3.7 / 37 | 0.97 / 9 / 1.7 / 27 | 0.97 / 6 / 0.6 / 9 |
| sniper | 0.97 / 12 / 3.5 / 37 | 0.97 / 9 / 1.5 / 24 | 0.96 / 5 / 0.5 / 7 |
| sniper_long | 0.97 / 10 / 2.3 / 36 | 0.97 / 7 / 0.8 / 12 | 0.96 / 3 / 0.2 / 5 |
| sniper_swift | 0.97 / 9 / 1.5 / 24 | 0.96 / 5 / 0.5 / 7 | 0.96 / 2 / 0.1 / 4 |
| shotgun (Benelli, not moved) | 0.96 / 0 / 0 / 0 | same | same |

Placement (sensitive_lhg_markers.png): at 6 cm the support-hand point (blue) is still on each handguard / pump, ahead of the
magazine and the receiver. Not applied - awaiting approval.
