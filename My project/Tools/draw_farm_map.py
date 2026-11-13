"""Draws the hand-drawn farm map the player finds in the cellar (Assets/Art/Images/Farm Map.png).

Everything is drawn to scale from the scene layout (10 px per metre, north = world +z up). Labels are not baked in;
FarmMapSetup overlays localized UI labels, so the bounds below must match FarmMapView.WorldBounds.

    python Tools/draw_farm_map.py [trees.json]

trees.json is an optional [[x, z, heightScale, isDead], ...] dump of the terrain trees inside the bounds.
"""
import json
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

X0, X1, Z0, Z1 = -120.0, 112.0, -14.0, 154.0
PX = 10  # output pixels per metre
SS = 2   # supersampling while drawing
W, H = int((X1 - X0) * PX), int((Z1 - Z0) * PX)
S = PX * SS

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Art", "Images", "Farm Map.png")

rng = random.Random(1071)
nrng = np.random.default_rng(1071)


def P(x, z):
    return ((x - X0) * S, (Z1 - z) * S)


class Ink:
    def __init__(self):
        self.img = Image.new("L", (W * SS, H * SS), 0)
        self.d = ImageDraw.Draw(self.img)


ink = Ink()    # main pen
faint = Ink()  # light pencil-ish strokes: crop rows, stipple
red = Ink()    # someone circled the truck


# ---------- strokes ----------

def resample(pts, step):
    out = [pts[0]]
    for a, b in zip(pts, pts[1:]):
        n = max(1, int(math.dist(a, b) / step))
        for i in range(1, n + 1):
            t = i / n
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return out


def wobble(pts, amp=0.25, step=1.0, freq=0.35):
    pts = resample(pts, step)
    ph = [rng.uniform(0, 6.283) for _ in range(3)]
    out, s = [], 0.0
    for i, p in enumerate(pts):
        if i:
            s += math.dist(pts[i - 1], p)
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dz) or 1.0
        nx, nz = -dz / length, dx / length
        o = amp * (0.6 * math.sin(s * freq + ph[0]) + 0.3 * math.sin(s * freq * 2.3 + ph[1]) + 0.1 * math.sin(s * freq * 5.1 + ph[2]))
        out.append((p[0] + nx * o, p[1] + nz * o))
    return out


def stroke(layer, pts, width=0.35, amp=0.25, step=1.0, closed=False):
    if closed:
        pts = list(pts) + [pts[0]]
    xy = [P(*p) for p in wobble(pts, amp, step)]
    layer.d.line(xy, fill=255, width=max(1, int(width * S)), joint="curve")


def dashed(layer, pts, dash=1.6, gap=1.0, width=0.2, amp=0.12):
    pts = resample(pts, 0.25)
    run, on, seg = 0.0, True, [pts[0]]
    for a, b in zip(pts, pts[1:]):
        run += math.dist(a, b)
        seg.append(b)
        if run >= (dash if on else gap) * rng.uniform(0.8, 1.2):
            if on and len(seg) > 1:
                stroke(layer, seg, width, amp, 0.5)
            on, run, seg = not on, 0.0, [b]
    if on and len(seg) > 1:
        stroke(layer, seg, width, amp, 0.5)


def dotted(layer, pts, spacing=1.0, r=0.14):
    for p in resample(pts, spacing):
        x, y = P(p[0] + rng.uniform(-0.1, 0.1), p[1] + rng.uniform(-0.1, 0.1))
        rr = r * S * rng.uniform(0.8, 1.2)
        layer.d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=255)


def hand_poly(layer, corners, width=0.35, over=0.6, amp=0.15):
    """Closed shape drawn edge by edge, overshooting the corners like a quick pen sketch."""
    for a, b in zip(corners, corners[1:] + corners[:1]):
        dx, dz = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dz)
        ux, uz = dx / length, dz / length
        o1, o2 = rng.uniform(0.05, over), rng.uniform(0.05, over)
        stroke(layer, [(a[0] - ux * o1, a[1] - uz * o1), (b[0] + ux * o2, b[1] + uz * o2)], width, amp, 0.8)


def rect(x0, z0, x1, z1):
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


def hatch(layer, poly, spacing=0.8, angle=45.0, width=0.14):
    xs, zs = [p[0] for p in poly], [p[1] for p in poly]
    px0, py0 = P(min(xs) - 1, max(zs) + 1)
    px1, py1 = P(max(xs) + 1, min(zs) - 1)
    box = (int(px0), int(py0), int(px1) + 1, int(py1) + 1)
    size = (box[2] - box[0], box[3] - box[1])
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).polygon([(P(*p)[0] - box[0], P(*p)[1] - box[1]) for p in poly], fill=255)
    lines = Image.new("L", size, 0)
    ld = ImageDraw.Draw(lines)
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    reach = math.hypot(max(xs) - min(xs), max(zs) - min(zs)) / 2 + 1
    a = math.radians(angle)
    ux, uz = math.cos(a), math.sin(a)
    nx, nz = -uz, ux
    t = -reach
    while t <= reach:
        p0 = (cx + nx * t - ux * reach, cz + nz * t - uz * reach)
        p1 = (cx + nx * t + ux * reach, cz + nz * t + uz * reach)
        ld.line([(P(*p)[0] - box[0], P(*p)[1] - box[1]) for p in wobble([p0, p1], 0.06, 1.0)], fill=255, width=max(1, int(width * S)))
        t += spacing * rng.uniform(0.85, 1.15)
    region = layer.img.crop(box)
    layer.img.paste(ImageChops.lighter(region, ImageChops.multiply(lines, mask)), box[:2])


def stipple(layer, poly_test, x0, z0, x1, z1, density=0.6, r=0.1):
    count = int((x1 - x0) * (z1 - z0) * density)
    for _ in range(count):
        x, z = rng.uniform(x0, x1), rng.uniform(z0, z1)
        if not poly_test(x, z):
            continue
        px, py = P(x, z)
        rr = r * S * rng.uniform(0.6, 1.4)
        layer.d.ellipse([px - rr, py - rr, px + rr, py + rr], fill=255)


def offset_polyline(pts, d):
    out = []
    for i, p in enumerate(pts):
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dz) or 1.0
        out.append((p[0] - dz / length * d, p[1] + dx / length * d))
    return out


def rect_distance(r, x, z):
    dx = max(r[0] - x, 0, x - r[2])
    dz = max(r[1] - z, 0, z - r[3])
    return math.hypot(dx, dz)


# ---------- layout (mirrors FarmLayoutBuilder / DogPenSetup) ----------

ROAD_X, ROAD_HALF = -70.0, 3.5
DRIVEWAY = [(-19.0, 35.0), (-36.0, 35.5), (-52.0, 36.2), (-66.5, 36.5)]
PAD = (-19.0, 29.5, 3.0, 40.5)
CELLAR_PATH = [(-7.5, 24.6), (-7.4, 27.5), (-7.2, 29.5)]
PORCH_PATH = [(-7.5, 40.5), (-7.5, 44.2)]
GATE = (72.37, 39.55)
LANE = [(3.0, 35.2), (22.0, 36.0), (34.0, 39.0), (60.0, 39.5), (GATE[0] - 1.0, GATE[1])]
BASEMENT = (-12.0, 6.0, 15.0, 25.0)
STAIRWELL = (-9.05, 19.25, -5.95, 24.35)
HOUSE = (-23.6, 73.5, -2.6, 85.5)
FRONT_PORCH = (-23.7, 70.6, -2.5, 73.5)
PORCH_STEPS = (-15.0, 70.5, -11.6, 73.5)
BACK_PORCH = (-22.2, 85.5, -16.0, 88.5)
SMALL_HOUSE = (-95.1, 128.2, -83.2, 143.0)
PEN_FENCE = (72.3, 34.8, 79.7, 43.2)
PEN_FOOTPRINT = (68.9, 29.3, 84.4, 48.6)
PEN_CLEARING = 6.0
TRUCK_CENTRE, TRUCK_HEADING = (1.85, 49.1), (-0.970, 0.242)  # hood direction on the ground

FIELD_MARGIN = 3.5
FIELDS = [
    # rect, rows along z
    ((30.0, -260.0, 260.0, 260.0), True),
    ((-64.0, 102.0, 30.0, 260.0), False),
    ((-260.0, -260.0, -78.0, 260.0), True),
]


def in_clearing(x, z):
    return rect_distance(PEN_FOOTPRINT, x, z) < PEN_CLEARING + FIELD_MARGIN


def near_small_house(x, z):
    return rect_distance(SMALL_HOUSE, x, z) < 4.0


def draw_fields():
    # Field edges.
    stroke(ink, [(30.0, Z0 - 2), (30.0, Z1 + 2)], 0.22, 0.35, 1.5)
    stroke(ink, [(-64.0, 102.0), (30.0, 102.0)], 0.22, 0.35, 1.5)
    stroke(ink, [(-64.0, 102.0), (-64.0, Z1 + 2)], 0.22, 0.35, 1.5)
    stroke(ink, [(-78.0, Z0 - 2), (-78.0, Z1 + 2)], 0.22, 0.35, 1.5)

    for (fx0, fz0, fx1, fz1), along_z in FIELDS:
        ix0, iz0 = max(fx0 + FIELD_MARGIN, X0 - 2), max(fz0 + FIELD_MARGIN, Z0 - 2)
        ix1, iz1 = min(fx1 - FIELD_MARGIN, X1 + 2), min(fz1 - FIELD_MARGIN, Z1 + 2)
        spacing = 2.6
        across = (ix0, ix1) if along_z else (iz0, iz1)
        along = (iz0, iz1) if along_z else (ix0, ix1)
        c = across[0] + spacing * 0.5
        while c < across[1]:
            s = along[0]
            while s < along[1]:
                seg = rng.uniform(5.0, 18.0)
                e = min(s + seg, along[1])
                pts = []
                t = s
                while t <= e:
                    x, z = (c, t) if along_z else (t, c)
                    if in_clearing(x, z) or near_small_house(x, z):
                        if len(pts) > 1:
                            stroke(faint, pts, 0.12, 0.12, 1.0)
                        pts = []
                    else:
                        pts.append((x, z))
                    t += 0.8
                if len(pts) > 1:
                    stroke(faint, pts, 0.12, 0.12, 1.0)
                s = e + rng.uniform(0.0, 2.5)
            c += spacing * rng.uniform(0.9, 1.1)


def draw_roads():
    gap = (DRIVEWAY[-1][1] - 2.1, DRIVEWAY[-1][1] + 2.1)
    stroke(ink, [(ROAD_X - ROAD_HALF, Z0 - 2), (ROAD_X - ROAD_HALF, Z1 + 2)], 0.3, 0.3, 1.5)
    stroke(ink, [(ROAD_X + ROAD_HALF, Z0 - 2), (ROAD_X + ROAD_HALF, gap[0])], 0.3, 0.3, 1.5)
    stroke(ink, [(ROAD_X + ROAD_HALF, gap[1]), (ROAD_X + ROAD_HALF, Z1 + 2)], 0.3, 0.3, 1.5)
    stipple(faint, lambda x, z: True, ROAD_X - ROAD_HALF + 0.4, Z0, ROAD_X + ROAD_HALF - 0.4, Z1, 0.35)
    # Ditches beside the road.
    for side in (-1, 1):
        dashed(faint, [(ROAD_X + side * 6.5, Z0 - 2), (ROAD_X + side * 6.5, Z1 + 2)], 2.5, 1.6, 0.12, 0.2)

    for side in (-2.1, 2.1):
        stroke(ink, offset_polyline(DRIVEWAY, side), 0.26, 0.2, 1.2)
    stipple(faint, lambda x, z: abs(z - (35.0 + (x + 19.0) / -47.5 * 1.5)) < 1.7, -66.5, 33.5, -19.0, 38.5, 0.45)

    hand_poly(ink, rect(*PAD), 0.28, 0.8, 0.2)
    stipple(faint, lambda x, z: True, PAD[0] + 0.5, PAD[1] + 0.5, PAD[2] - 0.5, PAD[3] - 0.5, 0.5)

    dotted(ink, CELLAR_PATH, 0.9)
    dotted(ink, PORCH_PATH, 0.9)
    for side in (-0.85, 0.85):
        dashed(ink, offset_polyline(LANE, side), 2.2, 1.2, 0.16, 0.15)


def draw_house():
    for _ in range(2):
        hand_poly(ink, rect(*HOUSE), 0.4, 0.9, 0.12)
    ridge_z = (HOUSE[1] + HOUSE[3]) / 2
    stroke(ink, [(HOUSE[0], ridge_z), (HOUSE[2], ridge_z)], 0.3, 0.1)
    for x in (HOUSE[0], HOUSE[2]):
        stroke(ink, [(x, ridge_z), (x + (2.5 if x < HOUSE[2] else -2.5), HOUSE[3])], 0.22, 0.08)
        stroke(ink, [(x, ridge_z), (x + (2.5 if x < HOUSE[2] else -2.5), HOUSE[1])], 0.22, 0.08)
    hatch(ink, rect(HOUSE[0] + 0.4, ridge_z + 0.3, HOUSE[2] - 0.4, HOUSE[3] - 0.4), 0.75, 65.0, 0.13)
    hand_poly(ink, rect(*FRONT_PORCH), 0.24, 0.5, 0.1)
    for z in (PORCH_STEPS[1] - 0.6, PORCH_STEPS[1] - 1.2):
        stroke(ink, [(PORCH_STEPS[0], z), (PORCH_STEPS[2], z)], 0.18, 0.06)
    hand_poly(ink, rect(*BACK_PORCH), 0.24, 0.5, 0.1)
    # Chimney.
    hand_poly(ink, rect(-8.4, 81.0, -6.8, 82.6), 0.2, 0.3, 0.05)
    hatch(ink, rect(-8.3, 81.1, -6.9, 82.5), 0.35, 20.0, 0.12)


def draw_small_house():
    x0, z0, x1, z1 = SMALL_HOUSE
    for _ in range(2):
        hand_poly(ink, rect(x0 + 0.6, z0 + 0.6, x1 - 0.6, z1 - 0.6), 0.36, 0.8, 0.1)
    ridge_x = (x0 + x1) / 2
    stroke(ink, [(ridge_x, z0 + 0.6), (ridge_x, z1 - 0.6)], 0.28, 0.08)
    hatch(ink, rect(ridge_x + 0.3, z0 + 1.0, x1 - 1.0, z1 - 1.0), 0.7, -30.0, 0.13)
    # Door step facing the road.
    hand_poly(ink, rect(x1 - 0.6, 133.6, x1 + 1.2, 136.0), 0.2, 0.3, 0.05)


def draw_basement():
    x0, z0, x1, z1 = BASEMENT
    dashed(ink, [(x0, z0), (x1, z0), (x1, z1), (x0, z1), (x0, z0)], 1.4, 0.9, 0.2, 0.08)
    hatch(faint, rect(x0 + 0.5, z0 + 0.5, x1 - 0.5, z1 - 0.5), 1.6, -40.0, 0.1)
    sx0, sz0, sx1, sz1 = STAIRWELL
    for _ in range(2):
        hand_poly(ink, rect(sx0, sz0, sx1, sz1), 0.3, 0.4, 0.06)
    z = sz0 + 0.5
    while z < sz1 - 0.2:
        stroke(ink, [(sx0 + 0.15, z), (sx1 - 0.15, z)], 0.16, 0.04)
        z += 0.55
    # Arrow up the steps, out of the cellar door.
    stroke(ink, [(-7.5, 20.0), (-7.5, 26.6)], 0.24, 0.06)
    stroke(ink, [(-8.5, 25.4), (-7.5, 26.8), (-6.5, 25.4)], 0.24, 0.05)


def draw_pen():
    x0, z0, x1, z1 = PEN_FENCE
    gz0, gz1 = GATE[1] - 0.8, GATE[1] + 0.8
    sides = [
        [(x0, z0), (x1, z0)], [(x1, z0), (x1, z1)], [(x1, z1), (x0, z1)],
        [(x0, z1), (x0, gz1)], [(x0, gz0), (x0, z0)],
    ]
    for a, b in sides:
        stroke(ink, [a, b], 0.26, 0.08, 0.6)
        n = max(2, int(math.dist(a, b) / 0.9))
        for i in range(n + 1):
            t = i / n
            x, z = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            dx, dz = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dz)
            nx, nz = -dz / length * 0.35, dx / length * 0.35
            stroke(ink, [(x - nx, z - nz), (x + nx, z + nz)], 0.14, 0.02, 0.3)
    # Gate swung open, outward.
    stroke(ink, [(x0, gz1), (x0 - 1.5, gz1 + 0.4)], 0.22, 0.03, 0.3)
    # Kennel against the back fence.
    kx0, kz0, kx1, kz1 = x1 - 2.6, 39.0, x1 - 0.6, 41.6
    hand_poly(ink, rect(kx0, kz0, kx1, kz1), 0.24, 0.3, 0.05)
    stroke(ink, [(kx0, (kz0 + kz1) / 2), (kx1, (kz0 + kz1) / 2)], 0.2, 0.04)
    hatch(ink, rect(kx0 + 0.15, (kz0 + kz1) / 2 + 0.1, kx1 - 0.15, kz1 - 0.15), 0.3, 50.0, 0.1)
    # Paw prints heading out of the gate.
    for i, (px, pz) in enumerate([(70.6, 39.1), (69.2, 40.0), (67.6, 39.3), (66.1, 40.2)]):
        cx, cy = P(px, pz)
        r = 0.32 * S
        ink.d.ellipse([cx - r, cy - r * 0.8, cx + r, cy + r * 0.8], fill=255)
        for ang in (-60, -20, 20, 60):
            a = math.radians(ang - 90)
            tx, ty = cx + math.cos(a) * r * 1.6, cy + math.sin(a) * r * 1.6
            tr = r * 0.32
            ink.d.ellipse([tx - tr, ty - tr, tx + tr, ty + tr], fill=255)


def draw_truck():
    (cx, cz), (hx, hz) = TRUCK_CENTRE, TRUCK_HEADING
    sx, sz = -hz, hx

    def at(f, s):
        return (cx + hx * f + sx * s, cz + hz * f + sz * s)

    half_len, half_w = 2.65, 1.0
    for _ in range(2):
        hand_poly(ink, [at(half_len, -half_w), at(half_len, half_w), at(-half_len, half_w), at(-half_len, -half_w)], 0.28, 0.3, 0.05)
    stroke(ink, [at(1.3, -half_w), at(1.3, half_w)], 0.22, 0.03)
    stroke(ink, [at(-0.1, -half_w), at(-0.1, half_w)], 0.22, 0.03)
    hatch(ink, [at(1.3, -half_w), at(1.3, half_w), at(-0.1, half_w), at(-0.1, -half_w)], 0.3, 30.0, 0.1)
    for f in (1.8, -1.6):
        for s in (-1.15, 1.15):
            hand_poly(ink, [at(f + 0.4, s - 0.15), at(f + 0.4, s + 0.15), at(f - 0.4, s + 0.15), at(f - 0.4, s - 0.15)], 0.18, 0.1, 0.02)
    # Loose loop around it in red pencil.
    loop = []
    for i in range(30):
        a = i / 26 * math.tau
        loop.append((cx + math.cos(a) * 5.0 * (1 + 0.04 * math.sin(a * 3)), cz + math.sin(a) * 3.6))
    stroke(red, loop, 0.3, 0.35, 0.7)


def draw_tree(x, z, scale, dead):
    if dead:
        r = 1.0 + scale * 1.6
        cx, cy = P(x, z)
        ink.d.ellipse([cx - 0.18 * S, cy - 0.18 * S, cx + 0.18 * S, cy + 0.18 * S], fill=255)
        for i in range(rng.randint(5, 7)):
            a = rng.uniform(0, math.tau)
            ex, ez = x + math.cos(a) * r, z + math.sin(a) * r
            stroke(ink, [(x, z), (ex, ez)], 0.12, 0.15, 0.4)
            fa = a + rng.choice((-0.6, 0.6))
            mx, mz = x + math.cos(a) * r * 0.6, z + math.sin(a) * r * 0.6
            stroke(ink, [(mx, mz), (mx + math.cos(fa) * r * 0.4, mz + math.sin(fa) * r * 0.4)], 0.1, 0.05, 0.3)
    else:
        r = 1.3 + scale * 2.2
        pts = []
        n = rng.randint(7, 10)
        start = rng.uniform(0, math.tau)
        for i in range(n + 1):
            a = start + i / n * math.tau
            rr = r * rng.uniform(0.8, 1.1)
            pts.append((x + math.cos(a) * rr, z + math.sin(a) * rr))
        stroke(ink, pts, 0.16, 0.25, 0.5)
        # Shade the south-east side.
        for i in range(4):
            a = math.radians(-60 + i * 18)
            stroke(faint, [(x + math.cos(a) * r * 0.3, z + math.sin(a) * r * 0.3), (x + math.cos(a) * r * 0.85, z + math.sin(a) * r * 0.85)], 0.12, 0.05, 0.3)
        cx, cy = P(x, z)
        ink.d.ellipse([cx - 0.15 * S, cy - 0.15 * S, cx + 0.15 * S, cy + 0.15 * S], fill=255)


def draw_compass():
    cx, cz = X1 - 12.0, Z1 - 16.0
    stroke(ink, [(cx, cz - 6.0), (cx, cz + 6.0)], 0.3, 0.05)
    stroke(ink, [(cx - 1.6, cz + 3.8), (cx, cz + 6.4), (cx + 1.6, cz + 3.8)], 0.3, 0.05)
    stroke(ink, [(cx - 3.5, cz), (cx + 3.5, cz)], 0.2, 0.05)
    # "N", drawn as strokes so the map carries no font.
    stroke(ink, [(cx - 1.0, cz + 8.0), (cx - 1.0, cz + 10.8), (cx + 1.0, cz + 8.0), (cx + 1.0, cz + 10.8)], 0.28, 0.05, 0.3)


# ---------- paper ----------

def value_noise(w, h, cell, seed_rng):
    gw, gh = max(2, w // cell + 2), max(2, h // cell + 2)
    grid = (seed_rng.random((gh, gw)) * 255).astype(np.uint8)
    return np.asarray(Image.fromarray(grid, "L").resize((w, h), Image.BICUBIC), dtype=np.float32) / 255.0


def paper():
    base = np.array([214, 196, 156], dtype=np.float32)
    n = (value_noise(W, H, 220, nrng) - 0.5) * 0.10 + (value_noise(W, H, 40, nrng) - 0.5) * 0.06 + (nrng.random((H, W)) - 0.5) * 0.04
    img = base[None, None, :] * (1.0 + n[..., None])

    # Darker, browner toward the edges.
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    edge = np.minimum.reduce([xx, W - 1 - xx, yy, H - 1 - yy]) / (0.12 * min(W, H))
    vign = np.clip(edge, 0, 1) ** 0.6
    img *= (0.72 + 0.28 * vign)[..., None]
    img[..., 2] *= (0.9 + 0.1 * vign)

    # Folds: thirds across, half down.
    for fx in (W / 3, 2 * W / 3):
        d = xx - fx + (value_noise(W, H, 300, nrng) - 0.5) * 6
        img *= (1.0 - 0.10 * np.exp(-(d / 2.0) ** 2) + 0.05 * np.exp(-((d - 5) / 4.0) ** 2))[..., None]
    d = yy - H / 2 + (value_noise(W, H, 300, nrng) - 0.5) * 6
    img *= (1.0 - 0.10 * np.exp(-(d / 2.0) ** 2) + 0.05 * np.exp(-((d - 5) / 4.0) ** 2))[..., None]

    # Stains.
    # Soft and broken, so no stain reads as something drawn on the map.
    for _ in range(4):
        cx, cy = nrng.uniform(0.1, 0.9) * W, nrng.uniform(0.1, 0.9) * H
        r = nrng.uniform(60, 160)
        dist = np.hypot(xx - cx, yy - cy) * (1 + (value_noise(W, H, 50, nrng) - 0.5) * 0.6)
        blot = np.clip(1 - dist / r, 0, 1) ** 1.5 * 0.07
        ring = np.exp(-((dist - r) / 7.0) ** 2) * 0.05 * value_noise(W, H, 40, nrng)
        img *= (1.0 - blot - ring)[..., None]
        img[..., 2] *= (1.0 - (blot + ring) * 0.6)

    # Torn, uneven border.
    tear = (value_noise(W, H, 25, nrng) - 0.5) * 22 + (value_noise(W, H, 6, nrng) - 0.5) * 6
    inside = np.minimum.reduce([xx, W - 1 - xx, yy, H - 1 - yy]) - 18 + tear
    alpha = np.clip(inside / 2.5, 0, 1)
    scorch = np.clip(1 - inside / 26.0, 0, 1) ** 2
    img *= (1.0 - 0.45 * scorch)[..., None]
    return np.clip(img, 0, 255), alpha


def ink_alpha(layer, strength, blur):
    small = layer.img.resize((W, H), Image.LANCZOS)
    if blur:
        small = small.filter(ImageFilter.GaussianBlur(blur))
    a = np.asarray(small, dtype=np.float32) / 255.0
    flow = 0.78 + 0.22 * value_noise(W, H, 14, nrng)
    return np.clip(a * flow * strength, 0, 1)


def main():
    trees = []
    if len(sys.argv) > 1:
        with open(sys.argv[1]) as f:
            trees = json.load(f)

    draw_fields()
    draw_roads()
    draw_basement()
    draw_house()
    draw_small_house()
    draw_pen()
    draw_truck()
    for x, z, scale, dead in trees:
        if rect_distance(HOUSE, x, z) < 1.0 or rect_distance(SMALL_HOUSE, x, z) < 1.0:
            continue
        draw_tree(x, z, scale, bool(dead))
    draw_compass()

    img, alpha = paper()
    for layer, color, strength, blur in (
        (faint, (92, 74, 52), 0.55, 0.6),
        (ink, (38, 28, 20), 0.92, 0.5),
        (red, (138, 30, 22), 0.8, 0.7),
    ):
        a = ink_alpha(layer, strength, blur)[..., None]
        img = img * (1 - a) + np.array(color, dtype=np.float32)[None, None, :] * a

    rgba = np.dstack([np.clip(img, 0, 255), alpha * 255]).astype(np.uint8)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray(rgba, "RGBA").save(OUT)
    print(f"Wrote {os.path.normpath(OUT)} ({W}x{H}), {len(trees)} trees.")


if __name__ == "__main__":
    main()
