# Zoop Test Plan

Use this checklist after any zoop-related change to verify preview validity, placement behavior, and cursor state restoration still match the game.

## Setup

- [ ] Load a save with access to pipes, cables, chutes, frames, walls, and the relevant replacement tools.
- [ ] Prepare one clean test area and one area containing existing small-grid and wall-grid pieces.
- [ ] Test both normal mode and authoring mode.
- [ ] For every scenario below, verify both preview state and final placement result.

## Core Flow

- [ ] Start and place a zoop for pipe.
- [ ] Start and place a zoop for cable.
- [ ] Start and place a zoop for chute.
- [ ] Start and place a zoop for frame.
- [ ] Start and place a zoop for wall.
- [ ] Cancel a zoop with right click before placement.
- [ ] Start a new zoop immediately after canceling a previous one.
- [ ] Verify no stale preview pieces remain after cancel or placement.

## Single Piece Parity

- [ ] Compare manual single placement vs one-piece zoop for cable.
- [ ] Compare manual single placement vs one-piece zoop for pipe.
- [ ] Compare manual single placement vs one-piece zoop for chute.
- [ ] Compare manual single placement vs one-piece zoop for wall.
- [ ] Confirm preview validity and final result match manual placement.

## Straight Small-Grid Zoops

- [ ] Zoop cables along X, Y, and Z.
- [ ] Zoop pipes along X, Y, and Z.
- [ ] Zoop chutes along X, Y, and Z.
- [ ] Verify spacing, orientation, and piece count.

## Corner and Waypoint Behavior

- [ ] Create a two-segment zoop with one corner.
- [ ] Create a three-segment zoop with two corners.
- [ ] Add multiple waypoints in one zoop.
- [ ] Remove the last waypoint and verify the preview updates correctly.
- [ ] Confirm no duplicated or missing segment appears at turns.

## Wall / Large Grid

- [ ] Zoop walls across one axis on a flat plane.
- [ ] Zoop walls across both axes on a flat plane.
- [ ] Drag a wall zoop off-plane and verify it stays clamped to the starting face.
- [ ] Try placing over an existing wall on the same face.
- [ ] Try placing on a different face of the same cell if manual placement allows it.

## Resource and Timing Behavior

- [ ] In normal mode, zoop more pieces than available materials allow.
- [ ] Confirm preview/final placement respect resource limits.
- [ ] Confirm long zoops still use the expected build timing in normal mode.
- [ ] Confirm authoring placements remain instant.
- [ ] Interrupting the build during the wait window restores a valid preview/manual placement state.

## Normal Mode Merge / Replacement Rules

- [ ] Cable over existing cable without the correct tool is blocked in preview.
- [ ] Cable over existing cable without the correct tool is blocked on placement.
- [ ] Cable over existing cable with the correct tool matches manual placement behavior.
- [ ] Pipe over existing pipe without the correct tool is blocked in preview.
- [ ] Pipe over existing pipe without the correct tool is blocked on placement.
- [ ] Pipe over existing pipe with the correct tool matches manual placement behavior.
- [ ] Chute overlap behavior matches manual placement behavior.
- [ ] Different cable subtypes still obey manual merge restrictions.
- [ ] Different pipe types/content still obey manual merge restrictions.

## Small-Grid Device Interaction

- [ ] Place pipes through compatible mounted pipe devices.
- [ ] Place pipes through incompatible mounted pipe devices.
- [ ] Place cables through compatible cable-mounted devices.
- [ ] Place cables through blocked device cases.
- [ ] Confirm each result matches manual placement behavior.

## Preview Accuracy

- [ ] Red preview always means final placement is blocked.
- [ ] Green/yellow preview always means final placement succeeds when clicked.
- [ ] No case exists where preview is green but nothing is placed.
- [ ] No case exists where preview is red but placement still succeeds.

## Authoring Mode Parity

- [ ] Repeat overlap/merge tests in authoring mode.
- [ ] Compare manual authoring placement vs zoop behavior.
- [ ] Confirm authoring-only legal overlaps are allowed by zoop.
- [ ] Confirm authoring-only illegal overlaps are still blocked by zoop.

## Rotation and Orientation

- [ ] Rotate before starting zoop and verify preview orientation.
- [ ] Rotate while placing and verify preview orientation.
- [ ] Verify walls keep the correct face/orientation.
- [ ] Verify pipes, cables, and chutes keep the correct orientation across turns.

## Cursor / State Restoration

- [ ] After canceling zoop, normal manual placement still works.
- [ ] After a failed zoop preview, manual placement still works.
- [ ] Change selected build option after zooping and verify the cursor is correct.
- [ ] Change hands/tools after zooping and verify no stale cursor state remains.
- [ ] Enter and exit authoring mode after zooping and verify cursor state is correct.

## Stress / Stability

- [ ] Long straight zoop with many segments updates correctly.
- [ ] Multi-corner zoop with many waypoints updates correctly.
- [ ] Rapid cursor movement during zoop does not break preview.
- [ ] Rapid add/remove waypoint input does not break preview.
- [ ] Repeated start/cancel/start does not leave stale state behind.

## Logs

- [ ] Inspect logs after the test pass.
- [ ] Confirm no repeated exceptions are emitted during preview updates.
- [ ] Confirm no cursor/placement validation errors appear unexpectedly.

---

## Bulk Deconstruction Mode

### Setup

- [ ] Build a test network with ~50 cables connected together
- [ ] Build a test network with ~50 pipes connected together
- [ ] Build a test network with ~20 chutes connected together
- [ ] Prepare wire cutters for cable testing
- [ ] Prepare wrench for pipe/chute testing

### UI Indicator Tests

- [ ] Press bulk deconstruct key (default: N) without any tool → mode should not activate
- [ ] Equip wire cutters and press bulk deconstruct key → status indicator appears above hand inventory
- [ ] Status indicator shows "Bulk Deconstruction" and "Activated" in green
- [ ] Status indicator is positioned 10px above the hand inventory (no overlap)
- [ ] Switch to another tool → mode deactivates and indicator disappears
- [ ] Switch hands → mode deactivates and indicator disappears

### Tooltip Integration Tests

- [ ] Activate bulk mode with wire cutters
- Aim at a cable
- [ ] Tooltip shows "Network Size: X" where X is the network element count
- [ ] Tooltip shows "Status: OK" in green when network is safe to deconstruct
- [ ] Connect cable to powered network → tooltip shows "Status: INVALID" in red
- [ ] Tooltip shows "Status: INVALID" reason when hovering invalid bulk
- [ ] Aim away from bulk → tooltip restores to normal
- [ ] Deactivate mode → tooltip restores to normal

### Detection and Validation Tests

- [ ] Aim at cable with wire cutters → detects full cable network
- [ ] Aim at pipe with wrench → detects full pipe network  
- [ ] Aim at chute with wrench → detects full chute network
- [ ] Aim at cable with wrench → no detection (wrong tool)
- [ ] Aim at pipe with wire cutters → no detection (wrong tool)
- [ ] Create powered cable network → validation shows "INVALID - Bulk is powered"
- [ ] Create pressurized pipe network (>50kPa) → validation shows "INVALID - Bulk under pressure"
- [ ] Safe cable network → validation shows "OK"
- [ ] Safe pipe network → validation shows "OK"

### Deconstruction Tests

- [ ] Click on valid cable bulk → first cable deconstructs normally (items spawn)
- [ ] Remaining cables should deconstruct progressively
- [ ] All cable items should be spawned in stacks
- [ ] Click on valid pipe bulk → first pipe deconstructs normally
- [ ] All pipe items should be spawned in stacks
- [ ] Verify no items are lost during bulk deconstruction
- [ ] Verify no duplicate items are created
- [ ] Mode deactivates automatically after successful deconstruction

### Performance Tests

- [ ] Aim at very large network (500+ cables) → no lag, no freeze
- [ ] Detection completes in reasonable time
- [ ] Rapid aiming between different bulks → smooth, no stuttering
- [ ] Check logs for "Using cached bulk" messages when re-aiming at same network
- [ ] Raycast throttling: smooth detection with no visual delay
- [ ] No "Collection was modified" exceptions in logs

### Edge Cases

- [ ] Aim at single isolated cable → bulk size = 1, still works
- [ ] Mixed network (cables + pipes) → only detects matching type
- [ ] T-junction cable network → detects all connected cables
- [ ] Complex branching network → detects entire network
- [ ] Network crossing world grid boundaries → detects correctly
- [ ] Deactivate mode while aiming at bulk → tooltip restores immediately
- [ ] Save/load while in bulk mode → mode state persists correctly
- [ ] Quit to menu while in bulk mode → no errors on cleanup

### Item Recovery Validation

- [ ] Deconstruct 10 cables → verify exactly 10 cable items recovered
- [ ] Deconstruct 20 pipes → verify exactly 20 pipe items recovered
- [ ] Large bulk (100+ items) → verify all items recovered

### Thread-Safety and Stability

- [ ] No "Collection was modified" exceptions during network tick
- [ ] No stack overflow on networks with 1000+ elements
- [ ] Bulk detection during heavy game activity (power/gas flow) → stable
- [ ] Multiple bulk operations in rapid succession → stable
- [ ] No memory leaks after extended bulk mode usage

### Integration with Zoop

- [ ] Bulk mode doesn't interfere with normal zoop placement mode
- [ ] No cursor state conflicts between modes
- [ ] No preview conflicts between modes

## Minimum Regression Suite

If you only run a short pass after a small change, cover at least these:

- [ ] Single-piece zoop matches manual placement for cable, pipe, and wall.
- [ ] Straight cable zoop works on empty cells.
- [ ] Cable over existing cable without the correct tool is blocked.
- [ ] Cable over existing cable with the correct tool matches manual behavior.
- [ ] Pipe over existing pipe without the correct tool is blocked.
- [ ] Authoring-mode overlap behavior matches manual authoring placement.
- [ ] One corner zoop works for cable or pipe.
- [ ] Wall zoop plane clamping still works.
- [ ] Canceling zoop does not break manual placement.
- [ ] Logs stay clean.

### Bulk Deconstruction Quick Check

- [ ] Activate bulk mode with wire cutters → status indicator appears
- [ ] Aim at cable network → tooltip shows "Cable Bulk" and network size
- [ ] Safe network → tooltip shows "Status: OK" in green
- [ ] Powered network → tooltip shows "Status: INVALID" in red
- [ ] Click valid cable bulk → all cables deconstruct and items are recovered
- [ ] Aim at large network (100+ elements) → no lag or freeze
- [ ] Switch tool → mode deactivates automatically
- [ ] No "Collection was modified" exceptions in logs
- [ ] Bulk mode doesn't interfere with normal zoop placement

