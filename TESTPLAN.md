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
