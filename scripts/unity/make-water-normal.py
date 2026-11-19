#!/usr/bin/env python3
"""Resources/Maps/water_normal.png: a seamless normal map of small sea ripples (sum of sine waves on whole-number
wave vectors, so it tiles), made here (no third-party image). Scrolled two ways over the sea in the game.

    python3 scripts/unity/make-water-normal.py
"""
import numpy as np
from PIL import Image
N = 512; rng = np.random.default_rng(20261119)
y, x = np.mgrid[0:N, 0:N] / N
gx = np.zeros((N, N)); gy = np.zeros((N, N))
for i in range(28):
    k = rng.integers(2, 20); a = rng.uniform(0, 2 * np.pi)
    kx, ky = int(round(np.cos(a) * k)), int(round(np.sin(a) * k))
    if kx == 0 and ky == 0: continue
    amp = 1.0 / (abs(kx) + abs(ky)) ** 1.1; ph = rng.uniform(0, 2 * np.pi)
    arg = 2 * np.pi * (kx * x + ky * y) + ph
    # sharper crests: the derivative of sin^3-ish shaped waves
    c = np.cos(arg) * (0.6 + 0.4 * np.sin(arg) ** 2)
    gx += amp * 2 * np.pi * kx * c; gy += amp * 2 * np.pi * ky * c
s = 0.045
n = np.dstack([-gx * s, -gy * s, np.ones_like(gx)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save('unity/Assets/LastWar/Resources/Maps/water_normal.png')
print('water_normal.png', N, 'px; mean tilt', round(float(np.degrees(np.arccos(n[..., 2])).mean()), 1), 'deg')
