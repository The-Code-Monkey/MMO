# MMO

A server-authoritative 3D MMO: clients express wishes, the server decides what happens, and every
player sees the same world. This glossary fixes the words the project uses for movement, cameras,
pointing at the world and the shape of the world, so a design discussion cannot quietly mean two
different things.

## Movement

**Intent**:
A client's expressed wish about what its character should do, which the server validates before
anything happens. Intent is never a fact about the world.
_Avoid_: Command, action, input (input is the raw device signal, before it becomes intent)

**Direct-drive intent**:
Movement intent that is re-stated for as long as a key is held, rather than naming a place to go.
_Avoid_: Continuous input, WASD movement, real-time movement

**Destination intent**:
Movement intent that names a place the character should end up, leaving the route to the server.
_Avoid_: Move order, waypoint, click target

**Server-authoritative mover**:
The single owner of a character's position and heading. Nothing else may move a character.
_Avoid_: Controller, motor, locomotion

**Heading**:
The direction a character faces on the horizontal plane. It is not a look direction and not a
velocity; a character can face one way and travel another.
_Avoid_: Rotation, facing (as a noun), forward (a direction, not a heading)

**Grounded**:
A character standing on a surface it could jump from. Leaving the ground by any means ends it.
_Avoid_: On floor, touching ground

**Air momentum**:
Horizontal motion a character keeps while airborne, which new movement intent cannot change.
_Avoid_: Air control, inertia

## Camera

**Orbit arm**:
The line between the camera pivot and the camera, whose length is the zoom distance.
_Avoid_: Boom, spring arm, follow distance

**Pivot**:
The point the camera orbits and looks at, fixed relative to the character rather than to the world.
_Avoid_: Look-at target, focus point, anchor

**Neck height**:
The pivot's fixed height above a character's feet, chosen so the view sits at the neck rather than
at the centre of the body.
_Avoid_: Eye height, head offset, camera height

**Orbit**:
Turning the camera around the pivot without turning the character. The camera's own yaw and pitch
are not the character's heading.
_Avoid_: Look, mouse-look, rotate camera (ambiguous with turning the character)

**Camera clip**:
The camera passing through world geometry. Prevented by shortening the orbit arm, never by moving
the pivot.
_Avoid_: Camera collision, occlusion

## Pointer

**World click**:
A press and release over the world that did not become a drag, resolved to whatever the pointer was
over.
_Avoid_: Click (ambiguous with UI clicks), interaction click, selection

**Click-versus-drag**:
The rule that separates a world click from an orbit drag by how far the pointer travelled between
press and release. One gesture, two meanings, decided on release.
_Avoid_: Drag threshold, dead zone

**Cursor capture**:
Hiding and confining the pointer while the player is steering the camera, so the pointer cannot leave
the window. It is not a game state and it does not pause anything.
_Avoid_: Mouse lock, pointer lock, focus mode

**Out-of-range advisory**:
A message telling the player that what they clicked is beyond reach. It is advice, not a refusal; the
server still decides, and the request is still made.
_Avoid_: Error, rejection, failure

## Zones

**Zone**:
One server process running one scene, with its own `NetworkManager` and its own connected players.
Players move between zones through portals, not by walking off the edge of the world.
_Avoid_: Map, level, world (the whole game is the world, not a zone)

**Zone risk tier**:
The label on a zone that decides what dying there costs, from `Safe` through `Yellow` and `Red` to
`Black`. It is about death consequences and PvP, never about resource rarity.
_Avoid_: Zone difficulty, zone colour, tier (bare), blue zone (the genre word for `Yellow`)

**Choke point**:
A narrow zone — a canyon pass, a bridge, a mountain path — that a neighbouring zone's only portal
runs through, so it can be fought over and held. It is a zone, not a flag on a portal.
_Avoid_: Bottleneck marker, gate, portal flag
