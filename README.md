# Wirenook Adventure Tools

Hand tools for exploring VRChat worlds with a group: a laser pointer, a
megaphone, a flashlight, a lantern, and notices that appear on everyone's
screen.

Package: `net.wirenook.adventure-tools`. Needs the VRChat Avatars SDK 3.10.6
or newer, [Modular Avatar](https://modular-avatar.nadena.dev/) and
[`net.wirenook.assets`](https://github.com/CassidyPrather/wirenook-assets).

## What is in it

Drag a prefab onto an avatar; Modular Avatar adds its menu entry, parameter
and animator at build time. Each tool works on its own, and
`Wirenook Adventure Tools` puts all five behind one "Tools" menu entry.

| Prefab | Hand | What it does | Synced bits | Rank cost |
| --- | --- | --- | --- | --- |
| `Wirenook Laser Pointer` | Right | A beam from the pointer's tip to whatever it hits, with a dot on the surface. | 1 | 1 raycast |
| `Wirenook Megaphone` | Right | A megaphone with pulsing sound waves and a two-tone bleep when raised. | 1 | 1 audio source |
| `Wirenook Flashlight` | Left | A torch with a visible beam. It does not light the world. | 1 | none |
| `Wirenook Flashlight (Real Light)` | Left | The same torch with a real spotlight. | 1 | 1 light: the avatar ranks **Poor** |
| `Wirenook Lantern` | Left | A lantern that swings from its handle and glows. | 1 | 1 PhysBone |
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

### Flashlight and real light

VRChat ranks an avatar with any light on it as Poor, enabled or not. The
default flashlight therefore draws its beam without lighting anything. Use
the Real Light prefab when you actually need to see in a dark world and
accept the rank.

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

Picking a notice shows it for 4.5 seconds, then your own client clears the
choice. The cards are drawn from `Textures/notices.json` by
`tools/notice_atlas.py`; add your own by appending to that list and
rebuilding.

## Limits and unknowns

- Not yet run in the VRChat client. The notice shader has only been
  checked in the Unity editor, not in a headset.
- Two tools in the same hand can be out at once and will overlap.
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
