# Wirenook Adventure Tools

Hand tools for exploring VRChat worlds with a group: a laser pointer, a
megaphone, a flashlight, a lantern, and notices that appear on everyone's
screen. The flashlight and lantern each come in a second build that shines
a real light and can be left standing in the world.

Also three things for whoever runs a game or watches one: an
[invisible switch](#the-invisible-switch);
[presence](#presence), which decides for each person in the room whether
they see you and whether they hear you; and
[the game master's kit](#the-game-masters-kit): dice, a pause, zone
markers, a pointer, a portal and a skeleton key.

Package: `net.wirenook.adventure-tools`. Needs the VRChat Avatars SDK 3.10.6
or newer, [Modular Avatar](https://modular-avatar.nadena.dev/),
[`net.wirenook.assets`](https://github.com/CassidyPrather/wirenook-assets),
[`net.wirenook.world-sync`](https://github.com/CassidyPrather/world-sync) and
[`net.wirenook.selective-animation`](https://github.com/CassidyPrather/selective-animation).

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

`Wirenook Notice (Roleplay)` is a second set, for running a scene ("Scene
starts", "Scene ends", "Roll for initiative", "Your turn", "One at a
time", "Time passes", "Short break", "Gather round"), from
`Textures/notices-roleplay.json`. It is called "Say" in the menu and has a
synced number of its own (`WN/Say`), so an avatar can carry both.
`Notices.Build` in `Editor/Notices.cs` makes a set from a list.

### Android and iOS

In a build for Android or iOS (see `net.wirenook.assets`), every tool is
kept except the notices and the beacon, which are drawn by custom shaders
that mobile avatars may not use. The tools get VRChat's mobile shaders and lose their
sound, and a Real Light build loses its light, since VRChat disables audio
sources and lights on mobile avatars: the tool is still held and can still
be placed, dark. Parameters stay as they are, so the avatar syncs the same
way on every platform.

## The invisible switch

| Prefab | What it does | Synced bits | Rank cost |
| --- | --- | --- | --- |
| `Wirenook Unseen` | "Invisible" in the menu: nobody else's client draws anything of the avatar. | 1 | none |

`Wirenook Unseen` puts one entry in the menu, "Invisible". While it is on,
nobody else's client draws anything of the avatar: its body, what it holds,
and what it has set down in the world. It is one synced bit and one
animator layer, with no component of any kind, so it costs an avatar
nothing in rank, and it works the same on PC, Android and iOS.

- **You still see yourself.** Your own client goes on drawing you, hands
  and all; the menu entry is how you know the switch is on. A mirror shows
  you too, since it is your client that draws it.
- **It does not touch your voice or your nameplate.** People hear you and
  see your name over nothing. [Presence](#presence) is the one that
  decides who hears.
- **It works by switching every renderer off**, through a stand-in that
  the build spreads to each renderer on the avatar
  (`WirenookAvatarRenderers` in `net.wirenook.assets`). Nothing else on an
  avatar may animate whether a renderer is enabled; tools are shown and
  hidden by scale.
- **A real light is put out too**, for everyone but you: the flashlight
  and lantern of the Real Light builds read the same switch.
- **Every avatar starts undrawn for a tenth of a second** after it loads,
  on every client. That is for avatars that load hidden from most people
  (below): what says so is itself worked out a frame or two after
  loading, and nobody is to see the avatar on the way.

In [cass-avatar](https://github.com/CassidyPrather/cass-avatar) every
variant of every avatar carries it, by rule, like the "AFK" switch.

## Presence

`Wirenook Presence` lets its wearer choose **who sees them** and **who
hears them**, separately, and each viewer's own client works out which
side of each choice it is on. It is for the people who run a game and the
people who watch one: a game master who narrates unseen, steps out to
speak to one player, or is there for everyone; a spectator nobody in the
scene can see or hear.

Needs [`net.wirenook.selective-animation`](https://github.com/CassidyPrather/selective-animation)
(the pointer, the latch and the circle it is built on) and the invisible
switch on the same avatar.

**None of it has run in VRChat.** What the editor can check, it checks
(see [What has and has not been checked of presence](#what-has-and-has-not-been-checked-of-presence)).

| Prefab | Loads as | Synced bits | What counts for rank |
| --- | --- | --- | --- |
| `Wirenook Presence` | seen and heard by everyone | 8 | 2 raycasts, 4 constraints, 1 contact; on PC 160 triangles and 4 bones for the reticle, joined to the avatar's mesh |
| `Wirenook Presence (Ghost)` | seen and heard backstage only, and undrawn as it rests | 8 | the same |

Four of the eight bits are presence's own, two for each choice; the other
four are the selector's (aim, mark, unmark, clear).

### The two choices

Each of "Shown to" and "Heard by" is one of:

- **Everyone.**
- **Backstage only.** Backstage is a circle: everyone whose avatar
  carries presence is in it, and its members always see and hear each
  other whatever anyone has chosen. Game masters and spectators are
  backstage together.
- **Chosen people.** Backstage, and whoever the wearer has pointed at and
  marked (Menu > Presence > Choose: aim with your head, hold Mark on
  someone until the reticle shows a green disc; Unmark and Clear all undo
  it. The selective-animation README has the details and the limits: who
  is chosen is held on each viewer's own client, and is lost when their
  copy of your avatar reloads).

Five pairs have a menu entry of their own:

| Preset | Shown to | Heard by | For |
| --- | --- | --- | --- |
| Everyone | everyone | everyone | being there |
| Ghost | backstage | backstage | watching, or setting a scene up, unnoticed |
| Narrate | backstage | everyone | a voice from nowhere |
| Whisper | backstage | chosen | a word in one player's ear |
| Appear | chosen | chosen | being there for one player and not the rest |

### What a viewer gets

- **Not shown to you:** nothing of the avatar is drawn (this is the
  invisible switch's layer, told by presence).
- **Backstage, or the wearer, while the wearer is not shown to
  everyone:** a spectre in place of the avatar, made of light in the
  wearer's signature colour (`net.wirenook.assets`), so that in a room
  full of game masters and spectators it is plain at a glance who the
  players cannot see. The wearer sees their own hands that way, which is
  how they know. On PC the avatar's own shader draws it, with bands of
  light climbing through it and every other pixel left out so the room
  shows through. On Android and iOS it is a flat shape of the same
  coloured light.
- **Chosen, and not backstage:** the avatar as it is.
- **Not to hear the wearer:** silence, and no nameplate. See below.

### How "heard by" works, and what it costs you

An avatar cannot mute its wearer. VRChat does put a player's voice at
their avatar's humanoid head bone, though, so on the client of each
person who is not to hear the wearer, presence moves that bone two
thousand kilometres straight up (a world can raise how far a voice
carries to a million metres at most). Their own client never moves it.

- **This only works on a rig built for it**: one whose humanoid head bone
  carries nothing, with the head people see on another bone that follows
  it (`WirenookFreeHead` in `net.wirenook.assets`; the `meta` avatar of
  cass-avatar is one). On any other avatar the build leaves the mover
  out and says so, and "Heard by" then does nothing.
- **The nameplate goes with the voice.** VRChat hangs it on the same
  bone. To someone who cannot hear you, your name is not there either.
- **It is unintended behaviour.** VRChat documents no way to move a voice,
  and may change where it puts one. If it does, "Heard by" stops working
  and nothing else does.
- **Check VRChat's rules before using it outside a game everyone has
  agreed to.** An avatar that hides its wearer's nameplate may be against
  the Community Guidelines; this package's author could not confirm the
  wording. Among people who expect an unseen game master it is the whole
  point; among strangers it is at best rude.
- **A viewer who has your avatar's animations switched off hears you**,
  and sees you, unless the avatar rests undrawn: `Wirenook Presence
  (Ghost)` does, so a spectator stays unseen even then.

### Building on it

Other features can ask each viewer's client what it was told, in their
own animators. These are animator parameters, written by animation on
every client for itself; none is synced, and on an avatar without
presence they rest at nought.

| Parameter | Kind | Above a half on a client where |
| --- | --- | --- |
| `WN/Hidden` | float | the avatar is not drawn at all |
| `WN/Spectral` | float | it is drawn as a spectre |
| `WN/Muted` | float | the wearer is not heard |

`WN/Unseen` is the invisible switch's own synced bool. In code
(`Wirenook.AdventureTools.Editor`), `Presence.DrawnHere`,
`Presence.NotDrawnHere`, `Presence.SpectralHere` and `Presence.HeardHere`
are those as audiences (see selective-animation), the first two with the
switch counted in; `presence.Backstage.Members` is the circle, for
something only the people backstage should see. A renderer needs none of
them: whatever the avatar draws is hidden with it. They are for what is
not a renderer, as the real lights do it.

`Presence.Configure(root, options, ...)` puts presence on a prefab of
your own: `PresenceOptions` has what it loads as, whether it rests
undrawn, the circle's name and the selector's options.

### What has and has not been checked of presence

Checked in the editor (`Tools > Wirenook > Adventure Tools > Verify Presence`):

- **The rule against a model of itself.** `PresenceModel` is who sees and
  who hears in plain code, with tests of its own. The generated animators
  are then played for each kind of viewer (the wearer, someone backstage,
  someone chosen, a stranger, someone both) under each of the nine pairs
  of choices, and what they do is held against it: what they tell the
  invisible switch, whether the spectre is drawn and with what, whether
  the head is moved, and never the wearer's own.
- **Loading.** A stranger's client is told a ghost is hidden from the
  first frame on, and the invisible switch draws nothing before it has
  been told.
- **The wiring**: eight synced bits, the defaults each prefab loads with,
  the head's mover (inactive at rest, far enough), the mobile spectre's
  shader.
- **In a build** (cass-avatar's `verify`): the switch's layer reaches
  every renderer the optimiser left, the mover's target is the humanoid
  head bone, nothing hangs under that bone, and on mobile no animation
  swaps in a shader VRChat refuses.

Not checked, because only a client can: that a voice and a nameplate
follow the head bone and are gone at that distance; that a client stops
drawing renderers switched off this way on other people's avatars; what
the spectre looks like in a headset (every other pixel left out may
shimmer); and everything the circle and the pointer rest on, for which
selective-animation has a probe to wear.

## The game master's kit

`Wirenook Game Master` is everything for running a scene, behind one
"Game Master" entry in the menu: dice, a pause, zone markers, a light and
a laser on the fingertip, a portal with two ends, a skeleton key for
contacts, and notices for a table. It is one prefab because its parts share
three things: the stage that everything left in the world stands on, the
pointer it is aimed with, and two meshes made in code.

Everything it shows is in the wearer's
[signature colour](https://github.com/CassidyPrather/wirenook-assets), on
every platform, where the avatar has the dial for one.

| Tool | Menu | What it does | Synced bits |
| --- | --- | --- | --- |
| Dice | Dice ▸ | A coin, 1d6, 2d6, 1d20, 2d20, for everyone or in secret. | 21 |
| Pause | Pause ▸ | A ring dropped where you stand; inside it the room is dimmed and people are told. | 1 of the stage's |
| Zones | Zones ▸ | Three spheres to set down, sized by a dial, for everyone or for backstage only. | 3, and 3 of the stage's |
| Pointer | Pointer ▸ | A light on the index fingertip, and a laser from it. | 3 |
| Portal | Portal ▸ | A door, and where it leads: a second spot, or you. | 2 of the stage's |
| Skeleton key | Key ▸ | Sends any one of VRChat's nineteen trait tags, and shows which it feels. PC only. | 9 |
| Roleplay notices | Say ▸ | Eight cards for a table ("Scene starts", "Roll for initiative", ...). PC only. | 8 |
| The stage | Clear all | Sets all of the above down in the world, in the same place for everyone, and carries a value for each thing (the sizes). | 66 |

116 synced bits in all, of the 256 an avatar may have. It was 140: the
four sizes were a synced float each, and now ride the bus for eight bits
between them (below). What it costs in
rank, on PC: one raycast, 20 contacts (all the key's), 15 constraints
(none deeper than three), one station, three audio sources, two skinned
meshes with five material slots between them, a trail and the notices'
card, and about 11,200 triangles, of which half are the shapes only PC
draws. On Android and iOS it is one skinned mesh in one material, about
5,600 triangles, one raycast and no contacts. On the `meta` avatar of
[cass-avatar](https://github.com/CassidyPrather/cass-avatar), with
presence, flight and a seat as well, that comes to Medium on every
platform.

### Setting things down

Zones, the pause and both ends of the portal stand in the world, where
you put them, for everyone, including whoever arrives later. All of them
ride one anchor bus of
[world-sync](https://github.com/CassidyPrather/world-sync), which is why
six things cost the synced bits of one position.

Pick "Aim" for a thing and you alone see it, riding the spot your pointer
is on: the laser's dot, or a step in front of you when it points at
nothing. Close the pointing hand into a fist, or press "Set down", and it
stays. About two seconds later everyone sees it. In between, your client
measures the spot, the bus reads it out ahead of its turn, and only then
is the thing switched on, so nobody is ever shown a thing where it used
to stand. "Shown" switches it off and on again without moving it, and
"Clear all" takes everything away.

Things stand upright in the world's own axes. The bus syncs no heading,
so everything here is round.

Every thing also has **a value**, a number from 0 to 1 that all clients
read under one name: the size of a zone or of the pause. The wearer sets
it with a dial; everyone else gets it from the bus, in the thing's turn,
and ahead of its turn when it has just changed. So a turn of a size dial
reaches other people a second or two later, in steps and not smoothly,
and all the dials together sync eight bits where they used to sync
eight each. world-sync's README has the mechanism and what it costs.

### Dice

Each roll is a button. Your client draws the numbers and syncs them, so
everyone is shown the same ones: the total large, each of two dice under
it, and which dice they were. A coin says HEADS or TAILS.

A roll is shown on a board over your head that reads from in front and
from behind. With "Roll in secret" on, nobody else is shown it, unless
"Backstage sees secrets" is on too, and then the people who share the
backstage circle with you are (see [presence](#presence)). You always
have a small board of your own in front of your chest, since nobody can
read a board over their own head. A roll stays up for twenty seconds, or
until the next, or "Take it down".

A public roll is shown whether or not you are: a game master who is
narrating unseen can still roll for the table.

### Pause

"Pause here" drops a ring where you stand, sized by its dial (one to
sixteen metres), with a pause sign turning over the middle and a sound
from the spot when it starts and when it ends. Everyone sees that much on
every platform, also with your shaders blocked: it is plain geometry.

On PC the inside of the ring is dimmed as well, and everyone whose eyes
are inside it gets a card on their screen saying the game is paused, for
as long as it is. You are not dimmed: the dimming leaves alone whatever
marks itself as a wirenook avatar (a stencil value its materials write),
which means other avatars from this family are not dimmed either.

"Paused" ends it.

### Zones

A zone is a sphere, drawn as a ring of dashes on the ground and two
dashed arches over it, drifting slowly round. On PC it is also what was
asked for: a dashed line lying on whatever surfaces the sphere's edge
crosses, floor, walls and furniture alike, and a faint tint on everything
inside.

That needs no depth texture, which a world may or may not provide, and no
light on the avatar to force one (a light is a rank). The shader,
`Wirenook/Stage Volume`, counts in one bit of the stencil buffer the way
shadow volumes do: a ball gives the tint, and the dashes are a thin shell
of that ball cut into slices, of which every other one is kept. Three
bits are used (16, 32 and 64) and each is cleared again by the pass that
draws.

- Zones that overlap leave the overlap untinted, and dashes that cross
  cancel where they cross: everything drawn in one go shares one bit.
- Another shader that leaves those bits set (a world's own stencil
  effect) shows up as tint.

"Backstage only" shows a zone to you and the people who share the
backstage circle, and to nobody else.

### Pointer

"Finger light" puts a glow on the last joint of your right index finger,
with a trail behind it on PC. With "Light when I point" on (it is, to
start with, and the choice is kept), the pointing gesture lights it too.
"Laser" adds a beam from the fingertip to a dot on whatever it meets,
world or player, thirty metres at most. Every viewer's client casts that
ray for itself from the hand as it sees it, so nothing of it is synced.

The ray runs the way the hand points, not the finger: a fist is how
things are set down, and a ray along a curled finger would point at your
palm. All of it is drawn only where you are (see
[the invisible switch](#the-invisible-switch)).

### Portal

"Aim the door" and set it down: a round gate anyone can click. With
nothing else set down it leads to you, a step behind wherever you have
got to, for the player who was left behind. "Aim the way out" sets down a
second end, and the door leads there instead. The traveller arrives
standing and lets go by walking. It is one of the six stations an avatar
may have.

### Skeleton key

A plain key in the left hand. "Send" chooses one of VRChat's nineteen
[built-in trait tags](https://creators.vrchat.com/common-components/contacts/built-in-contact-tags/)
(Fire, Hot, Cold, Freezer, Water, Wet, Wind, Damage, DamageBlunt,
DamageSharp, Weapon, Projectile, Shield, Ammunition, Consumable,
ConsumableFood, ConsumableDrink, Brush, Dye), and the key's head then
gives that trait off to whatever it touches: a campfire of
[`net.wirenook.contacts`](https://github.com/CassidyPrather/contacts),
somebody's avatar, a world. A stone in the middle of its dial shows the
family being sent. It sends whether or not you are shown.

It also feels for all nineteen at once. Round the dial is a pip for each,
in its family's colour, in the order above, starting at the far end of
the dial and going round towards the fingers; a pip is out while
something within reach gives that trait off, which is how to find out
what a thing sends.

A sender's tags cannot be changed while it is worn, so there are nineteen
senders and one is on. Android and iOS allow eight contacts before an
avatar is ranked Poor, so mobile builds leave the contacts and the menu
entry out.

### A kit for one module

The kit is a list of tools, and a game master made for one module is the
same prefab built with more tools in the list.

```csharp
GameMasterBuild.Materials();
Stage.Prepare();
var tools = GameMasterBuild.Kit();     // dice, pause, zones, pointer, portal, key
tools.Add(new MyTrap());               // an IStageTool
GameMasterBuild.Build(prefabPath, controllerPath, meshPrefix, new GameMasterOptions { Tools = tools });
```

A tool (`IStageTool`) is asked four things in turn, every tool's first
before any tool's second:

| Step | What it does there |
| --- | --- |
| `Things(plan)` | Names what it sets down: `plan.Thing("Trap")`, or `plan.Thing("Alarm", atFeet: true)` for something dropped where the wearer stands. `value:` is what the thing's value rests at, and `rehearsed: true` makes it part of a course (below). |
| `Build(kit)` | Makes its objects. `kit.Stage.Hold("Trap", kit.Stage["Trap"])` gives a bone that stands where the thing does and rides the wearer's aim while they place it. `kit.Solid` is the mesh every platform draws (`ShapeMesh`: boxes, rings, bends, balls, slices of a ball, in swatches of the wirenook palette, of which `glow-cyan` and `cyan` follow the signature colour). `kit.Effects` is the PC-only mesh; `kit.Effect(material)` gives a slot in it. |
| `Layers(kit)` | Adds its layers to `kit.Fx`. `kit.Stage.Show(fx, name, paths, audience)` pops bones in for an audience and keeps the meshes' bounds whole meanwhile. A thing has `Standing` (on show, for a client that knows where), `Mine` (the wearer, while placing it) and `Shown` (either) as audiences, to narrow with `And` (see [selective-animation](https://github.com/CassidyPrather/selective-animation)); `GameMaster.Backstage` is the people backstage and the wearer. |
| `Menu(kit)` | Adds its parameters to `kit.Parameters` and its folder with `kit.Folder(label, icon)`; `kit.Stage.MenuAim`, `MenuNow`, `MenuShown` and `MenuSet` are the stage's controls for a thing. |

**A value for each thing.** `thing.Value` is a float every client has
(the stage's bus carries it): read it with `MotionTime` for a size or a
heading, and give the wearer its dial with `kit.Stage.MenuValue`. It
costs no synced bits beyond the eight the bus spends on all of them, and
arrives a second or two after the wearer changes it.

**A course, rehearsed.** A thing asked for with `rehearsed: true` is
laid out behind the scenes: set down, it stands for the wearer and the
people backstage only, until the wearer raises the stage's one synced
`Live` bit (`kit.Stage.MenuLive`) and the whole course is there for
everyone at once. A thing then has three more audiences: `Real` (it
stands, and the course is live: the only audience its contacts should be
on for), `Rehearsal` (the people backstage, before it is live, who are
to see it as a spectre) and `Shown`, which is those two and `Mine`.
"Clear all" takes the course back behind the scenes.

**Standing in for the plain kit.** `GameMasterOptions.FeatureId` and
`Replaces` are for a kit that goes on an avatar made from one that
already carries the plain kit: it names `game-master` as what it
replaces (`WirenookReplaces`), and must keep the prefix `GM`, because
the parameters of a feature that is left out of a build stay declared.

`Wirenook/Stage Volume` takes a colour of its own (`_Colour`, with
`_Own` at 1) for a volume that should not be in the signature colour.

The worked example is in
[cass-avatar](https://github.com/CassidyPrather/cass-avatar),
`Packages/net.wirenook.meta-avatar/Editor/Crypt`: a kit for a module
called The Sunken Crypt, with zones of an element, a danger zone, a
barricade, a locked chest, a pendulum and a rift, in meshes and
materials of its own, on skill checks from
[contacts](https://github.com/CassidyPrather/contacts).

`ZoneTool.Marker(kit, bone)` adds one sphere marker, a metre in radius,
to both meshes, for a tool that marks a place of its own, and
`GameMasterBuild.Sized` adds the layer that scales a bone to a dial's
radius. `ShapeMesh.Vanishing` names a run of shapes as a blendshape that
gathers them to a point, which is how the dice lose a stroke without a
bone for every stroke. `Pointer` and `Stage` can also be configured on a
prefab of your own without the rest.

Both meshes are marked `WirenookApart` (net.wirenook.assets): the
invisible switch and presence leave them alone, so a hidden game master's
zones stay. What a tool shows of the wearer's own (the key, the light) it
narrows with `Presence.DrawnHere` itself.

### What has and has not been checked of the kit

Checked in the editor (`Tools > Wirenook > Adventure Tools > Verify Game Master`),
by playing the generated animator on a copy of the prefab for the wearer's
client and one for a viewer's, with parameter drivers carried out, the
bus's readings worked out and the synced parameters copied across ten
times a second (world-sync's check does the standing-in):

- **Setting down.** A thing set down with a fist is on show for everyone
  1.8 s after the click; moved with the menu's button, 2.5 s; the pause,
  1.9 s. At no moment is a viewer shown a thing anywhere but where it
  stood before or where it stands now. Someone who arrives afterwards has
  everything within one round of the bus (5.7 s for six things). The
  switch takes a thing away and brings it back in place; clearing reaches
  the viewer.
- **Dice.** The boards draw the right strokes for all four hundred pairs
  of two d20, for every single d20, and the right words for each roll.
  Forty throws of each roll come to numbers that roll can show.
- **Who is shown what**, for the wearer, someone backstage and a
  stranger: both dice boards under every combination of the two
  switches, a zone for backstage only, and the finger light under the
  switch, the gesture and the wearer not being drawn.
- **The key.** Each of the nineteen choices switches on its own sender
  and no other, and each pip follows its trait.
- **What it carries**: the counts above, and that nothing every platform
  draws is skinned to a bone mobile builds leave out.
- **The sizes on the bus**, by world-sync's check: every slot's value
  reaches a viewer with its place, and one the wearer changes is with
  the viewer a second later.

`Assets/Validation/Editor/GameMasterPreview.cs` in cass-avatar renders
every tool, the shaders included, and each new shader compiles
(`ShaderProbe`).

Not checked, because only a client can. For each, what to do to find out:

- **Whether the stencil effects draw at all in a client, and in a
  headset.** Set a zone down on a floor with furniture on it and look for
  the dashes on the furniture; then stand in it. If there is only the
  ring and the arches, the volumes are not drawing.
- **That things arrive where they were put.** Set a zone down at
  something recognisable, with a friend watching and another joining
  afterwards. This rests on everything world-sync's bus rests on.
- **That a raycast's result is in place before constraints read it.** If
  not, what you aim trails your hand by a frame.
- **The dice.** Roll 2d20 with a friend watching: that their board shows
  your numbers, and that the numbers vary. It rests on a parameter driver
  drawing whole numbers at random between two ends, both included.
- **The pause's card.** Stand inside with a friend outside: only the one
  inside should have the card.
- **The portal.** That a station whose arrival point is far from its
  collider takes a traveller there, as the catch-up portal of
  avatar-stations also assumes.
- **The key.** Hold it to something with a receiver for a trait (the
  campfire of net.wirenook.contacts takes Fire). That a contact on an
  object switched on by animation starts to send is how the contact
  tester of that package works too, and is as untried.
- **Where the light sits.** It is put on the last joint of the index
  finger, since rigs do not agree where a fingertip is. How that looks on
  a hand, and whether the beam leaves from the right place, wants looking
  at.

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
(`Wirenook.AdventureTools.Editor.AdventureToolsBuild.Rebuild`). The
invisible switch and presence are `Editor/PresenceBuild.cs`, which that
calls; `Rebuild Presence` in the same menu does them alone. The game
master's kit and the roleplay notices are `Editor/GameMasterBuild.cs`,
likewise called, with `Rebuild Game Master` to do them alone. The models
come from `blender/` (see `blender/README.md`); the kit has none, its
shapes are made in code.

Three tools write what the generators read:

```bash
python tools/notice_atlas.py     # every set of cards: notices, roleplay notices, the pause's card
python tools/stage_palette.py    # which swatches of the wirenook palette follow the signature colour
```

and the kit's sounds (`dice`, `pause`, `resume`) and menu icons are in
`net.wirenook.assets`, made by its `tools/sfx.py` and `tools/icons.py`.

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
then push a tag named for that version (`v` and the number). CI makes the GitHub release and attaches the `.zip`
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
