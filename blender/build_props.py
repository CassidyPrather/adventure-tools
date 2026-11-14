"""Builds the adventure-tools props: flashlight, lantern, megaphone, laser pointer.

    blender -b --python blender/build_props.py [-- --only STEM] [-- --previews DIR]

Each prop is modelled from the code below, saved as `blender/<stem>.blend`,
checked against the wirenook prop contract and exported to the package's
`Models/<stem>.fbx`. See README.md beside this file.
"""

import math
import pathlib
import sys

sys.dont_write_bytecode = True

REPO = pathlib.Path(__file__).resolve().parents[1]
MODELS = REPO / "Packages" / "net.wirenook.adventure-tools" / "Models"
LIBRARY = REPO.parent / "wirenook-assets" / "tools" / "blender"
if not (LIBRARY / "wirenook_props.py").is_file():
    raise SystemExit(f"The shared prop kit is missing: {LIBRARY / 'wirenook_props.py'}\n"
                     "Clone wirenook-assets beside this repository and run again.")
sys.path.insert(0, str(LIBRARY))

from mathutils import Vector

from wirenook_props import (FORWARD, GLOW, SOLID, UP, PropSpec, chamfered_rect, frame, main_build, rabbit_badge,
                            ring_points)

X = Vector((1.0, 0.0, 0.0))
BACK = -FORWARD
DOWN = -UP


def ahead(distance, height=0.0):
    """A point on the prop's centre line, `distance` forward of the hold point."""
    return FORWARD * distance + UP * height


# --------------------------------------------------------------------------
# Flashlight
# --------------------------------------------------------------------------

FLASHLIGHT = PropSpec("Flashlight", "flashlight", triangle_budget=450, parts={"Beam": None},
                      materials=(SOLID, GLOW), beams={"Beam": 1.0})


def build_flashlight(mesh):
    lens = 0.083
    body = mesh.part()
    # Half a step of phase puts a flat on top for the switch to sit on.
    body.lathe(ahead(0.0), FORWARD, [
        (-0.072, 0.015), (-0.066, 0.021), (-0.034, 0.021), (0.022, 0.021),
        (0.022, 0.0255), (0.031, 0.0255), (0.035, 0.022),
        (0.064, 0.037), (0.067, 0.040), (0.083, 0.040), (0.088, 0.036), (0.088, 0.032),
        (lens, 0.030), (lens, 0.017),
    ], [
        "ink", "ink", "lavender",
        "orange", "orange", "orange",
        "ice", "ink", "ink", "ink", "ink",
        "silver", "glow-warm",
    ], segments=12, phase=0.5, cap_start="ink", cap_end="glow-white")

    switch_base = chamfered_rect(ahead(0.002, 0.0195), X, BACK, 0.0065, 0.012, 0.003)
    switch_top = chamfered_rect(ahead(0.002, 0.0265), X, BACK, 0.0045, 0.010, 0.002)
    body.loft([switch_base, switch_top], "orange", cap_end="orange")

    beam = mesh.part("Beam", GLOW)
    beam.lathe(ahead(lens), FORWARD, [(0.0, 0.028), (1.0, 0.35)], "glow-warm", segments=12, sharp="none")
    return {"Beam": ahead(lens)}


# --------------------------------------------------------------------------
# Lantern
# --------------------------------------------------------------------------

LANTERN = PropSpec("Lantern", "lantern", triangle_budget=600, parts={"Body": None, "Glow": "Body"},
                   materials=(SOLID, GLOW))


def build_lantern(mesh):
    ring_radius = 0.035
    pivot = DOWN * (2.0 * ring_radius)
    cap_top = pivot + DOWN * 0.010
    core = cap_top + DOWN * 0.088

    handle = mesh.part()
    tube = [(0.0055, 0.0), (0.0, 0.0055), (-0.0055, 0.0), (0.0, -0.0055)]
    handle.sweep(ring_points(DOWN * ring_radius, X, UP, ring_radius, ring_radius, 10, 0.5), BACK, tube,
                 "ink", closed=True)

    body = mesh.part("Body")
    # The eye the handle ring threads through: it arches over the ring's lowest point.
    eye = [cap_top + BACK * (0.013 * math.cos(a)) + UP * (-0.004 + 0.017 * math.sin(a))
           for a in (math.radians(d) for d in (0.0, 60.0, 120.0, 180.0))]
    body.sweep(eye, X, [(0.0045, 0.0), (-0.002, 0.004), (-0.002, -0.004)], "orange", cap_ends=False)

    # Walked downwards, with a flat facing forward for the badge.
    body.lathe(cap_top, DOWN, [(0.0, 0.016), (0.020, 0.050), (0.028, 0.053), (0.028, 0.042)],
               ["lavender", "ink", "ink"], segments=12, phase=0.5, cap_start="lavender")
    body.lathe(cap_top, DOWN, [(0.028, 0.042), (0.044, 0.060), (0.060, 0.066), (0.116, 0.066),
                               (0.132, 0.060), (0.148, 0.042)],
               ["glow-orange", "glow-warm", "glow-warm", "glow-warm", "glow-orange"],
               segments=12, phase=0.5, sharp=(0, 5))
    body.lathe(cap_top, DOWN, [(0.148, 0.042), (0.148, 0.053), (0.156, 0.053), (0.176, 0.020)],
               ["ink", "ink", "lavender"], segments=12, phase=0.5, cap_end="lavender")
    body.lathe(cap_top, DOWN, [(0.176, 0.010), (0.186, 0.011), (0.194, 0.0)], "orange", segments=6)

    # A camping lantern's guard: four bars, set so the front and back stay open.
    side, front, _ = frame(DOWN)
    for degrees in (30.0, 150.0, 210.0, 330.0):
        outward = side * math.cos(math.radians(degrees)) + front * math.sin(math.radians(degrees))
        bar = [cap_top + DOWN * d + outward * r
               for d, r in ((0.026, 0.051), (0.058, 0.070), (0.118, 0.070), (0.150, 0.051))]
        body.sweep(bar, outward.cross(DOWN), [(0.0035, 0.0), (-0.002, 0.0045), (-0.002, -0.0045)], "ink",
                   hard_section=True, cap_ends=False)

    face = 0.066 * math.cos(math.pi / 12.0) + 0.0015
    for facing in (FORWARD, BACK):
        rabbit_badge(body, core + facing * face + DOWN * 0.013, facing, 0.0125, "ink")

    glow = mesh.part("Glow", GLOW)
    glow.ellipsoid(core, (0.16, 0.16, 0.16), "glow-warm", segments=8, rings=5)
    return {"Body": pivot, "Glow": core}


# --------------------------------------------------------------------------
# Megaphone
# --------------------------------------------------------------------------

MEGAPHONE = PropSpec("Megaphone", "megaphone", triangle_budget=600, parts={"Waves": None},
                     materials=(SOLID, GLOW))


def build_megaphone(mesh):
    axis_height = 0.075
    mouth = 0.180
    body = mesh.part()
    body.lathe(ahead(0.0, axis_height), FORWARD, [
        (-0.100, 0.021), (-0.094, 0.028), (-0.080, 0.028),
        (-0.076, 0.035), (-0.012, 0.035), (-0.004, 0.028),
        (0.060, 0.046), (0.125, 0.068), (0.166, 0.091),
        (0.170, 0.097), (mouth, 0.097), (mouth, 0.089),
        (0.120, 0.060), (0.050, 0.030),
    ], [
        "orange", "orange",
        "lavender", "lavender", "ink",
        "ice", "ice", "ice",
        "orange", "orange", "orange",
        "violet", "violet",
    ], segments=12, cap_start="ink", cap_end="ink")
    body.lathe(ahead(0.0, axis_height), FORWARD, [(0.050, 0.015), (0.082, 0.011), (0.094, 0.0)],
               "lavender", segments=6, sharp=(0,))

    # A raked pistol grip, its top buried in the housing.
    grip = [chamfered_rect(ahead(-0.016, -0.052), X, BACK, 0.016, 0.021, 0.006),
            chamfered_rect(ahead(-0.014, -0.044), X, BACK, 0.0175, 0.023, 0.007),
            chamfered_rect(ahead(0.0, 0.050), X, BACK, 0.015, 0.020, 0.006)]
    body.loft(grip, "ink", cap_start="ink", smooth=True)
    trigger = [chamfered_rect(ahead(0.024, 0.014), X, BACK, 0.006, 0.007, 0.002),
               chamfered_rect(ahead(0.026, 0.040), X, BACK, 0.006, 0.010, 0.002)]
    body.loft(trigger, "orange", cap_start="orange")

    # Three shells of one sphere family centred inside the horn, so from any
    # side they read as the ))) of a loudspeaker icon and from the front as rings.
    waves = mesh.part("Waves", GLOW)
    centre = 0.090
    for radius, swatch in ((0.125, "glow-cyan"), (0.180, "glow-lavender"), (0.245, "glow-magenta")):
        shell = [(centre + radius * math.cos(a), radius * math.sin(a))
                 for a in (math.radians(d) for d in (40.0, 29.0, 18.0))]
        waves.lathe(ahead(0.0, axis_height), FORWARD, shell, swatch, segments=12, sharp="none")
    return {"Waves": ahead(mouth, axis_height)}


# --------------------------------------------------------------------------
# Laser pointer
# --------------------------------------------------------------------------

LASER_POINTER = PropSpec("LaserPointer", "laser-pointer", triangle_budget=300,
                         parts={"Beam": None, "Dot": None}, materials=(SOLID, GLOW), beams={"Beam": 1.0})


def build_laser_pointer(mesh):
    tip = 0.070
    body = mesh.part()
    body.lathe(ahead(0.0), FORWARD, [
        (-0.060, 0.0050), (-0.054, 0.0050), (-0.054, 0.0085), (0.008, 0.0085),
        (0.010, 0.0100), (0.040, 0.0100), (0.044, 0.0085),
        (0.060, 0.0058), (0.064, 0.0058), (0.064, 0.0042), (0.068, 0.0042), (tip, 0.0025),
    ], [
        "orange", "lavender", "lavender",
        "ink", "ink", "ink",
        "ice", "ice", "glow-magenta", "glow-magenta", "glow-magenta",
    ], segments=8, phase=0.5, cap_start="orange", cap_end="glow-magenta")

    # Pocket clip along the top flat, and the button under the thumb.
    clip = [[ahead(d, h) + X * x for x, h in ((-0.003, 0.0080), (0.003, 0.0080), (0.003, 0.0105), (-0.003, 0.0105))]
            for d in (-0.050, -0.008)]
    body.loft(clip, "ice", cap_start="ice", cap_end="ice")
    button = [chamfered_rect(ahead(0.025, 0.0090), X, BACK, 0.0035, 0.007, 0.0015),
              chamfered_rect(ahead(0.025, 0.0120), X, BACK, 0.0025, 0.006, 0.0012)]
    body.loft(button, "orange", cap_end="orange")

    beam = mesh.part("Beam", GLOW)
    beam.loft([ring_points(ahead(tip + d), X, UP, 0.002, 0.002, 4) for d in (0.0, 1.0)], "glow-magenta",
              cap_end="glow-magenta")

    # A Y2K sparkle rather than a plain disc; it faces back up the beam.
    dot = mesh.part("Dot", GLOW)
    star = [ahead(tip) + (X * math.cos(a) + UP * math.sin(a)) * r
            for a, r in ((math.radians(45.0 * k), 0.016 if k % 2 == 0 else 0.0045) for k in range(8))]
    dot.fan(ahead(tip), star, "glow-magenta", BACK)
    dot.polygon(ring_points(ahead(tip - 0.0005), X, UP, 0.006, 0.006, 4), "glow-white", BACK)
    return {"Beam": ahead(tip), "Dot": ahead(tip)}


PROPS = [
    (FLASHLIGHT, build_flashlight),
    (LANTERN, build_lantern),
    (MEGAPHONE, build_megaphone),
    (LASER_POINTER, build_laser_pointer),
]

if __name__ == "__main__":
    main_build(PROPS, REPO / "blender", MODELS)
