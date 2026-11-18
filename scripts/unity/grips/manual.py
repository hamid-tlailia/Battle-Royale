# Grip points read off each model's side profile (glTF scene space of the stored model: y up, z along the barrel, metres).
# R = right-hand grip (palm centre on the pistol grip / stock wrist), L = left hand (palm on the handguard / pump / fore-grip;
# pistols: cupping the grip), M = muzzle (barrel centre at the front end), A = aim point (sight line above the right hand).
P = {
 'rifle':            dict(R=(-0.035+0.035, 0.000, 0.010), L=(0, 0.060, -0.310), M=(0, 0.085, -0.590), A=(0, 0.135, -0.100)),
 'rifle_marksman':   dict(R=(0, -0.040, 0.010), L=(0, 0.000, -0.300), M=(0, 0.040, -0.670), A=(0, 0.110, -0.100)),
 'rifle_storm':      dict(R=(0, -0.035, 0.000), L=(0, 0.020, -0.270), M=(0, 0.050, -0.563), A=(0, 0.100, -0.080)),
 'shotgun':          dict(R=(0, -0.040, 0.000), L=(0, 0.005, -0.300), M=(0, 0.083, -0.677), A=(0, 0.120, -0.080)),  # Benelli M4 (stocked)
 'shotgun_breacher': dict(R=(0, -0.035, 0.000), L=(0, -0.005, -0.430), M=(0, 0.055, -0.670), A=(0, 0.080, -0.050)),
 'shotgun_long':     dict(R=(0, 0.025, 0.075), L=(0, 0.040, -0.430), M=(0, 0.085, -0.705), A=(0, 0.110, -0.100)),
 'smg':              dict(R=(0, -0.035, -0.065), L=(0, 0.055, -0.330), M=(0, 0.070, -0.470), A=(0, 0.120, -0.120)),
 'smg_hornet':       dict(R=(0, 0.000, -0.070), L=(0, -0.010, 0.100), M=(0, 0.060, 0.205), A=(0, 0.115, -0.030), flip=True),
 'smg_viper':        dict(R=(0, 0.000, -0.040), L=(0, 0.055, -0.240), M=(0, 0.095, -0.402), A=(0, 0.150, -0.120)),
 'sniper':           dict(R=(0, -0.050, 0.110), L=(0, -0.020, -0.330), M=(0, -0.005, -0.804), A=(0, 0.060, 0.050)),
 'sniper_long':      dict(R=(0, -0.075, 0.100), L=(0, -0.025, -0.320), M=(0, 0.000, -0.938), A=(0, 0.090, 0.000)),
 'sniper_swift':     dict(R=(0, -0.065, 0.075), L=(0, 0.000, -0.330), M=(0, 0.030, -0.838), A=(0, 0.100, 0.000)),
 'pistol':           dict(R=(0, -0.020, 0.008), L=(0, -0.035, 0.012), M=(0, 0.050, -0.154), A=(0, 0.072, -0.030)),
 'pistol_auto':      dict(R=(0, -0.020, 0.020), L=(0, -0.035, 0.024), M=(0, 0.067, -0.185), A=(0, 0.080, -0.020)),
 'pistol_magnum':    dict(R=(0, -0.025, 0.000), L=(0, -0.040, 0.004), M=(0, 0.050, -0.208), A=(0, 0.075, -0.030)),
}
