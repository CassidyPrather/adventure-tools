# Wirenook Adventure Tools

Hand tools for exploring VRChat worlds with a group: a laser pointer, a
megaphone, a flashlight, a lantern, and notices that appear on everyone's
screen. The flashlight and lantern each come in a second build that shines
a real light and can be left standing in the world.

Package: `net.wirenook.adventure-tools`. Needs the VRChat Avatars SDK 3.10.6
or newer, [Modular Avatar](https://modular-avatar.nadena.dev/) and
[`net.wirenook.assets`](https://github.com/CassidyPrather/wirenook-assets).

## What is in it

Drag a prefab onto an avatar; Modular Avatar adds its menu entry, parameter
and animator at build time. Each tool works on its own, and
`Wirenook Adventure Tools` puts the plain ones behind one "Tools" menu
entry. `Wirenook Adventure Tools (Real Flashlight)` and `(Real Lantern)`
are the same set with that one tool as a real light and without the other.
`Wirenook Adventure Tools (Event)` is the plain set for whoever is leading a
group: its laser pointer can ping, and it adds the beacon.

| Prefab | Hand | What it does | Synced bits | Rank cost |
| --- | --- | --- | --- | --- |
| `Wirenook Laser Pointer` | Right | A beam from the pointer's tip to whatever it hits, with a dot on the surface. | 1 | 1 raycast |
| `Wirenook Laser Pointer (Ping)` | Right | The same, and a click leaves a marker on the spot it points at for four seconds, with a chirp. | 2 | 1 raycast, 1 mesh, 1 material slot, 1 audio source, 1 constraint |
| `Wirenook Megaphone` | Right | A megaphone with pulsing sound waves and a two-tone bleep when raised. | 1 | 1 audio source |
| `Wirenook Flashlight` | Left | A torch with a visible beam. It does not light the world. | 8, shared | none |
| `Wirenook Lantern` | Left | A lantern that swings from its handle and glows. It does not light the world. | 8, shared | 1 PhysBone |
| `Wirenook Flashlight (Real Light)` | Left | The torch with a real spotlight. Can be placed in the world. | 58 | 1 light: the avatar ranks **Poor**; 1 mesh, constraints and contacts |
| `Wirenook Lantern (Real Light)` | Left | The lantern with a real point light. Can be placed in the world. | 58 | 1 light: the avatar ranks **Poor**; 2 meshes, 1 PhysBone, constraints and contacts |
| `Wirenook Beacon` | | A pin over your head that shows through walls, and an arrow in everyone else's view that points the way to you when the pin is out of it. | 1 | 1 mesh, 1 material slot; bounds of 160 m while it is on |
| `Wirenook Notice` | | Eight notices ("Portal dropping", "Respawn", ...) shown on the screen of everyone who can see you, with a chime. | 8 | 1 audio source, 1 mesh, larger bounds |

Tools appear with a small pop and shrink away rather than blinking in and
out. That is not only for looks: they are toggled by scaling a bone, so
their renderers never switch off, and an optimiser such as
[Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) can merge
every tool into the avatar's main mesh.

### Fitting the tools to your hands

Avatars agree on which bone is the hand but not on which way its axes
point, so the tools ship without a pose. After placing them, select the
avatar in the scene and run
`Tools > Wirenook > Assets > Fit Hand Grips To Selected Avatar`. It reads
your finger bones and seats every tool so that it points where the fingers
point. Adjust the `Grip` object under each tool's `Mount` by hand from
there if you like.

### Laser pointer

Uses the SDK's `VRCRaycast` (version 1.1), which runs on every viewer's
own client, so the beam length needs no synced parameters. It reaches 30 m
and hits world geometry and other players.

**Ping.** In the `(Ping)` build, closing the right hand into a fist while
the pointer is out (a trigger pull, on most controllers) stands a marker on
the spot the dot is on: a gem over two rings, with a thin column above it
to find it by from across a room. It lasts four seconds. The menu has a
"Ping" button as well, for controllers that track fingers and so never make
a fist by themselves, or always do. A ray that hits nothing does not ping.
The `(Ping)` build carries a `WirenookHandClaim` for the right hand while
the pointer is out: an avatar that reads those shows no facial expression
for that hand's gestures meanwhile, so the fist that pings is not also a
face.

One synced bit says "ping now", and every viewer's client stands the marker
where its own copy of the laser's dot is when that bit arrives. Hold the
hand still through the click and that is the same spot for everyone. Click
while sweeping and each viewer's marker lands wherever the dot had got to on
their screen, a network delay later. A marker that agreed exactly would need
a synced position (see `net.wirenook.world-sync`): sixty-six bits, and more
than half a second before it showed.

The marker stays put while the hand moves on, so it cannot be part of the
avatar's mesh. That is one mesh and one material slot, which is why the
plain build exists.

### Beacon

`Wirenook/Beacon` draws two things for everyone but you. A pin stands over
your head, in front of everything, and never gets smaller on screen than
about a twenty-fifth of its distance, so it can be picked out across a
large world. When the pin is out of someone's view, an arrow near the centre
of their view points the way to turn; for someone behind them it points
sideways, the way people turn round. Both fade out within three metres, where
they would only be in the way, and stop at 80 m. The arrow does not draw in
mirrors, handheld cameras or screenshots. Both are drawn from distance
functions in the shader; there is no texture.

Unity only draws a mesh whose bounds are in view, and the arrow is for
exactly when you are not, so the mesh's bounds are 160 m across. The object
rests switched off and scaled to nothing, which makes those bounds a point:
an avatar with the beacon off is no larger to cull or to rank than one
without it. While it is on, viewers within range draw two triangles for it
wherever they look.

### Flashlight, lantern and real light

Both are held in the left hand and answer to one shared parameter, so with
both on an avatar only one is out at a time: taking one out puts the other
away.

VRChat ranks an avatar with any light on it as Poor, enabled or not, and
with two as Very Poor. The plain builds therefore draw a beam cone and a
glow without lighting anything. The Real Light builds leave that geometry
out and shine an actual light, for when you need to see in a dark world and
accept the rank: a spotlight in the flashlight, a point light in the
lantern. Put at most one of them on an avatar. They cannot share a light,
because a light's type cannot be animated and a spotlight cannot stand in
for a point light.

A Real Light build has a "Place" switch that leaves the tool where your
hand is, lit, and picks it up again. The placed position is synced (see
`net.wirenook.world-sync`), so people who arrive later see it where you
left it. Putting the tool away picks it up too. The plain builds cannot be
placed: they are part of the avatar's mesh, which is what keeps them free
in performance rank, and a merged mesh disappears whenever the avatar is
out of view.

### Screen notices

`Wirenook/Screen Notice` is a small shader that draws a card a fixed
distance in front of each viewer instead of where its mesh is. In VR it is
placed from the midpoint between the eyes, so it sits at a real depth
rather than flat on the lenses. It does not draw in mirrors, handheld
cameras or screenshots, nor for viewers further than 30 m away, and it
hides itself if a viewer's safety settings block your shaders.

A viewer only sees the card while your avatar is roughly on their screen:
Unity does not draw a mesh whose bounds are out of view, and the bounds are
as large (3.6 m) as VRChat's Good rank allows.

Picking a notice fades it in, shows it for about four seconds and fades it
out, then your own client clears the choice. Switching it off early, or
picking another, skips straight to the fade-out; a card that is still
fading in finishes that first. The cards are drawn from `Textures/notices.json` by
`tools/notice_atlas.py`; add your own by appending to that list and
rebuilding.

### Android and iOS

In a build for Android or iOS (see `net.wirenook.assets`), every tool is
kept except the notices and the beacon, which are drawn by custom shaders
that mobile avatars may not use. The tools get VRChat's mobile shaders and lose their
sound, and a Real Light build loses its light, since VRChat disables audio
sources and lights on mobile avatars: the tool is still held and can still
be placed, dark. Parameters stay as they are, so the avatar syncs the same
way on every platform.

## Limits and unknowns

- Not yet run in the VRChat client. The notice shader has only been
  checked in the Unity editor, not in a headset.
- The real light, its placing and the reworked notice fades have only been
  checked in the Unity editor.
- The ping and the beacon have only been checked in the Unity editor: that
  they build, and where the beacon's shader puts the pin and the arrow from
  a set of camera positions. Whether a fist is a comfortable "click" on real
  controllers, how far apart viewers' markers land, and how the arrow sits
  in a headset are all untried.
- Whether the client caps a mesh's bounds on avatars has not been checked.
  If it does, the beacon's arrow only shows while the avatar is near the
  edge of view.
- The tools do not pose your fingers around them.

## Rebuilding the generated assets

Everything under `Generated/` and `Prefabs/` is written by
`Tools > Wirenook > Adventure Tools > Rebuild`
(`Wirenook.AdventureTools.Editor.AdventureToolsBuild.Rebuild`). The models
come from `blender/` (see `blender/README.md`) and the notice cards from
`tools/notice_atlas.py`.

## Development

Requires Unity 2022.3.22f1 (see `ProjectSettings/ProjectVersion.txt`) via a VPM
client (prefer [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)), and the
[.NET SDK](https://dotnet.microsoft.com/download) 11 for the headless checks.

The Unity project at the repository root is the development harness. It
takes `net.wirenook.assets` from a checkout of
[wirenook-assets](https://github.com/CassidyPrather/wirenook-assets) beside
this one (`../wirenook-assets`). The simplest way to get that layout is to
work in [cass-avatar](https://github.com/CassidyPrather/cass-avatar), which
mounts this repository and its siblings as submodules under `modules/`.

```bash
scripts/check-meta.sh          # every visible file has a committed .meta
scripts/check-manifests.py     # package.json sanity across Packages/
scripts/format.sh --check      # dotnet-format over the CI-visible C#
scripts/test.sh                # headless NUnit run of Runtime/Core + Tests
scripts/build-packages.sh      # VPM .zip + .unitypackage per package -> dist/
```

Pre-commit hook: `git config core.hooksPath .githooks`

Release: bump `version` in the package's `package.json`, commit and push,
then create a GitHub release tagged with that version. CI attaches the `.zip`
and `.unitypackage`; the listing at
[CassidyPrather/vpm](https://github.com/CassidyPrather/vpm) picks them up
once this repository is in its `githubRepos`.

## Legal

Copyright 2026 Cassidy Prather <cassidy@wirenook.net>

Everything in this repository henceforth is licensed under
[AGPLv3](https://www.gnu.org/licenses/agpl-3.0.html) (See `LICENSE`) unless
otherwise specified.

As a special exception, the copyright holders give permission to link this
Program with the VRChat SDK and with Unity Technologies' proprietary engine
components, and to convey the resulting work under terms of your choice,
provided that you also convey the Corresponding Source of this Program under
the terms of the GNU Affero General Public License version 3 in all other
respects. (This exception also in `LICENSE_ADDENDUM`.)

## Credits

Descended from https://github.com/vrchat-community/template-package
([VRCHAT DISTRO LICENSE FILE](https://github.com/vrchat-community/template-package/blob/d9cf13fe9f56867cbf7315a4dbbf1901bc1537ec/Packages/com.vrchat.core.bootstrap/License.md))

Third-party material is inventoried in [CREDITS.md](CREDITS.md).
