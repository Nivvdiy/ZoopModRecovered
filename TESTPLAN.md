# Zoop Smoke Test

Run this after any small zoop-related change.

## Core checks

- [ ] One-piece zoop matches manual placement for cable.
- [ ] One-piece zoop matches manual placement for pipe.
- [ ] Straight zoop works on empty cells.
- [ ] One blocked overlap case is still blocked correctly.
- [ ] One corner zoop still works.
- [ ] Canceling zoop does not break manual placement.
- [ ] No stale preview remains after cancel or placement.
- [ ] Logs stay clean.

## Add targeted checks only for the area you changed

### Overlap / validation
- [ ] Cable over existing cable without the correct tool is blocked.
- [ ] Cable over existing cable with the correct tool matches manual behavior.
- [ ] Pipe over existing pipe without the correct tool is blocked.
- [ ] Pipe over existing pipe with the correct tool matches manual behavior.

### Waypoints / pathing
- [ ] Multi-waypoint zoop updates correctly.
- [ ] Removing the last waypoint updates preview correctly.
- [ ] No duplicate or missing segment appears at turns.

### Wall placement
- [ ] Wall zoop works on a flat plane.
- [ ] Wall zoop stays clamped to the starting face.
- [ ] Wall orientation remains correct.

### Cursor / state restoration
- [ ] Canceling zoop restores manual placement correctly.
- [ ] Changing tool/build option after zooping restores the correct cursor.
- [ ] Entering/exiting authoring mode leaves no stale state.

### Resources / timing
- [ ] Zoop respects material limits in normal mode.
- [ ] Consumed materials match placed pieces.
- [ ] Authoring placement remains instant.

### Authoring mode
- [ ] Authoring-mode overlap behavior matches manual authoring placement.
- [ ] Authoring-only legal overlaps are allowed.
- [ ] Authoring-only illegal overlaps are still blocked.
