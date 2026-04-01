using System;
using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Planning;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Placement;

/// <summary>
/// Owns the small-grid zoop preview flow from path planning through preview instantiation and placement.
/// </summary>
internal sealed class ZoopSmallGridCoordinator(ZoopPreviewValidator previewValidator)
{
  private sealed class SmallGridPreviewLayoutAdapter(ZoopPreviewValidator previewValidator)
    : ISmallGridPreviewLayoutAdapter
  {
    public ZoopDraft Draft { get; set; }

    public Structure GetDraftPreviewStructure(int index)
    {
      return Draft.PreviewPieces[index].Structure;
    }

    public int GetDraftCellSpan(int index)
    {
      return Draft.PreviewPieces[index].CellSpan;
    }

    public void ApplyRotation(SmallGridRotationStep step)
    {
      if (!step.SupportsCornerVariant)
      {
        return;
      }

      var isSegmentTurnStart =
        (step.DirectionIndex > 0 || (step.SegmentIndex > 0 && step.DirectionIndex == 0)) && step.PlacementIndex == 0;
      if (isSegmentTurnStart)
      {
        if (step.LastDirection == step.ZoopDirection)
        {
          SetStraightRotation(GetAdapterPreviewStructure(step.StructureCounter), step.ZoopDirection);
        }
        else
        {
          SetCornerRotation(GetAdapterPreviewStructure(step.StructureCounter), step.LastDirection, step.IncreasingFrom,
            step.ZoopDirection, step.IncreasingTo);
        }

        return;
      }

      if (!step.IsSinglePlacement)
      {
        SetStraightRotation(GetAdapterPreviewStructure(step.StructureCounter), step.ZoopDirection);
      }
    }

    public bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
    {
      return previewValidator.CanConstructSmallCell(Draft, inventoryManager, structure, structureIndex);
    }

    public Vector3Int GetDraftCellKey(Vector3 position)
    {
      return ZoopPositionUtility.ToSmallGridCellKey(position);
    }

    private Structure GetAdapterPreviewStructure(int index)
    {
      return Draft.PreviewPieces[index].Structure;
    }

    private static void SetStraightRotation(Structure structure, ZoopDirection zoopDirection)
    {
      switch (zoopDirection)
      {
        case ZoopDirection.x:
          SetStructureRotation(structure, structure is Chute
            ? SmartRotate.RotX.Rotation
            : SmartRotate.RotY.Rotation);
          break;
        case ZoopDirection.y:
          SetStructureRotation(structure, structure is Chute
            ? SmartRotate.RotZ.Rotation
            : SmartRotate.RotX.Rotation);
          break;
        case ZoopDirection.z:
          SetStructureRotation(structure, structure is Chute
            ? SmartRotate.RotY.Rotation
            : SmartRotate.RotZ.Rotation);
          break;
        case ZoopDirection.none:
        default:
          throw new ArgumentOutOfRangeException(nameof(zoopDirection), zoopDirection, null);
      }
    }

    private static void SetCornerRotation(Structure structure, ZoopDirection zoopDirectionFrom, bool increasingFrom,
      ZoopDirection zoopDirectionTo, bool increasingTo)
    {
      var xOffset = 0.0f;
      var yOffset = 0.0f;
      var zOffset = 0.0f;
      if (structure.GetPrefabName().Equals("StructureCableCorner"))
      {
        xOffset = 180.0f;
      }

      if (structure.GetPrefabName().Equals("StructureChuteCorner"))
      {
        xOffset = -90.0f;
        switch (zoopDirectionTo)
        {
          case ZoopDirection.z when zoopDirectionFrom == ZoopDirection.x:
            yOffset = increasingTo ? -90.0f : 90f;
            break;
          case ZoopDirection.x when zoopDirectionFrom == ZoopDirection.z:
            yOffset = increasingFrom ? 90.0f : -90f;
            break;
          default:
            yOffset = 180.0f;
            break;
        }
      }

      SetStructureRotation(structure,
        ZoopUtils.GetCornerRotation(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo, xOffset, yOffset,
          zOffset));
    }

    private static void SetStructureRotation(Structure structure, Quaternion rotation)
    {
      structure.ThingTransformRotation = rotation;
      structure.transform.rotation = rotation;
    }
  }

  private readonly SmallGridPreviewLayoutAdapter layoutAdapter = new(previewValidator);

  /// <summary>
  /// Rebuilds the active small-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    Vector3 currentPos, List<ZoopSegment> segments, int spacing)
  {
    var isSinglePlacement = ZoopPathPlanner.BuildSmallGridPlan(draft.Waypoints, currentPos, segments);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    var supportsCornerVariant =
      ZoopConstructableRules.SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables,
        inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
    BuildSmallStructureList(draft, previewCache, inventoryManager, segments, supportsCornerVariant,
      barrierCells: null);

    if (draft.PreviewCount <= 0)
    {
      return;
    }

    layoutAdapter.Draft = draft;
    HashSet<(int seg, int dir)> blockedDirections = null;
    Dictionary<(int seg, int dir), HashSet<int>> errorCells = null;
    draft.HasError = draft.HasError || ZoopPreviewLayoutCoordinator.PositionSmallGridStructures(
      layoutAdapter,
      inventoryManager,
      segments,
      supportsCornerVariant,
      spacing,
      isSinglePlacement,
      out blockedDirections,
      out errorCells);

    // If any long piece is blocked (e.g. crossing an existing structure), rebuild
    // with the blocked cells as barriers so long variants resume after the obstacle.
    // Span-1 pieces can merge at these cells (T-junctions), but long pieces cannot,
    // so we use the error cells from pass 1 directly as barriers.
    if (blockedDirections is { Count: > 0 })
    {
      draft.HasError = false;

      BuildSmallStructureList(draft, previewCache, inventoryManager, segments, supportsCornerVariant,
        barrierCells: errorCells);

      if (draft.PreviewCount > 0)
      {
        layoutAdapter.Draft = draft;
        draft.HasError = ZoopPreviewLayoutCoordinator.PositionSmallGridStructures(
          layoutAdapter,
          inventoryManager,
          segments,
          supportsCornerVariant,
          spacing,
          isSinglePlacement,
          out _,
          out _);
      }
    }
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current small-grid path.
  /// When long variants are available (e.g. long-3, long-5, long-10 pipes), consecutive straight
  /// pieces in a direction are packed with the longest fitting variant first.
  /// First piece, last piece, and waypoint pieces are always single-cell.
  /// </summary>
  private static void BuildSmallStructureList(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager,
    List<ZoopSegment> segments,
    bool supportsCornerVariant,
    Dictionary<(int seg, int dir), HashSet<int>> barrierCells)
  {
    ZoopPreviewFactory.ResetSmallGridPreviewList(draft, previewCache);
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    var context = new ZoopPreviewContext(draft, previewCache, constructables, inventoryManager, supportsCornerVariant);

    bool AddPiece(bool isCorner, int index, int secondaryCount, bool currentCanBuildNext) =>
      ZoopPreviewFactory.AddStructure(context, isCorner, index, secondaryCount, currentCanBuildNext);

    var longVariants = ZoopLongVariantRules.FindLongVariants(constructables);
    var hasLongVariants = longVariants.Count > 0;
    var runPlan = new List<int>();
    var longCounts = new Dictionary<int, int>();

    var straight = 0;
    var corners = 0;
    var lastDirection = ZoopDirection.none;
    var canBuildNext = true;
    var totalSegmentCount = segments.Count;
    for (var segmentIndex = 0; segmentIndex < totalSegmentCount; segmentIndex++)
    {
      var segment = segments[segmentIndex];
      var totalDirectionCount = segment.Directions.Count;
      for (var directionIndex = 0; directionIndex < totalDirectionCount; directionIndex++)
      {
        var zoopDirection = segment.Directions[directionIndex];
        var zoopCounter = ZoopPathPlanner.GetCountForDirection(zoopDirection, segment);

        zoopCounter = ZoopPathPlanner.GetPlacementCount(totalSegmentCount, segmentIndex, totalDirectionCount,
          directionIndex, zoopCounter);

        // Determine if the first placement in this direction is a corner turn.
        var willHaveCorner = supportsCornerVariant &&
                             draft.PreviewCount > 0 &&
                             zoopDirection != lastDirection;

        if (willHaveCorner && zoopCounter > 0)
        {
          canBuildNext = AddPiece(isCorner: true, index: corners, secondaryCount: straight,
            currentCanBuildNext: canBuildNext);
          corners++;
          lastDirection = zoopDirection;
        }

        var straightInDir = willHaveCorner ? zoopCounter - 1 : zoopCounter;

        if (straightInDir > 0 && hasLongVariants)
        {
          var isGlobalFirst = segmentIndex == 0 && directionIndex == 0 && !willHaveCorner;
          var isGlobalLast = segmentIndex == totalSegmentCount - 1 && directionIndex == totalDirectionCount - 1;
          var isWaypointStart = segmentIndex > 0 && directionIndex == 0 && !willHaveCorner;

          // Detect whether the next direction will be a corner turn.
          var nextWillCorner = false;
          if (supportsCornerVariant && !isGlobalLast)
          {
            ZoopDirection nextDir;
            if (directionIndex + 1 < totalDirectionCount)
              nextDir = segment.Directions[directionIndex + 1];
            else if (segmentIndex + 1 < totalSegmentCount)
              nextDir = segments[segmentIndex + 1].Directions[0];
            else
              nextDir = zoopDirection;
            nextWillCorner = nextDir != zoopDirection;
          }

          // Build a unified separator set. Separators are always span-1 pieces that
          // act as section boundaries: start, end, corners, waypoints, and barriers.
          var separators = new HashSet<int>();

          if (isGlobalFirst || isWaypointStart)
            separators.Add(0);
          if (isGlobalLast || nextWillCorner)
            separators.Add(straightInDir - 1);

          // Barrier cells from pass 1 (merge points with existing structures).
          var dirKey = (segmentIndex, directionIndex);
          if (barrierCells != null && barrierCells.TryGetValue(dirKey, out var cellSet))
          {
            var cornerOffset = willHaveCorner ? 1 : 0;
            foreach (var cell in cellSet)
              separators.Add(cell - cornerOffset);
          }

          ZoopLongVariantRules.PlanSections(straightInDir, longVariants,
            separators.Count > 0 ? separators : null, runPlan);

          if (separators.Count > 0)
          {
            var sepStr = string.Join(",", separators);
            var planStr = string.Join(",", runPlan);
            ZoopLog.Debug($"[Sections] seg={segmentIndex} dir={directionIndex} straightInDir={straightInDir} " +
                          $"separators=[{sepStr}] plan=[{planStr}]");
          }

          for (var planIdx = 0; planIdx < runPlan.Count; planIdx++)
          {
            var cellSpan = runPlan[planIdx];
            if (cellSpan > 1)
            {
              // Before placing a long piece, verify we can also afford at least one more
              // span-1 piece after it — otherwise the preview would end on a long variant
              // which changes the build cursor and may cover fewer total cells than span-1 pieces.
              var isLastInPlan = planIdx == runPlan.Count - 1;
              var mustReserveTrailing = !isLastInPlan;

              var longBuildIndex = ZoopLongVariantRules.GetBuildIndexForSpan(longVariants, cellSpan);
              if (!longCounts.TryGetValue(cellSpan, out var longIndex))
                longIndex = 0;

              var skipLong = false;
              if (mustReserveTrailing && canBuildNext)
              {
                var activeHandItem = InventoryManager.ActiveHandSlot.Get();
                if (activeHandItem is Stackable stack)
                {
                  var longCost = longBuildIndex >= 0 && longBuildIndex < constructables.Count
                    ? ZoopPreviewFactory.GetEntryQuantity(constructables[longBuildIndex])
                    : cellSpan;
                  // Need budget for the long piece + at least 1 more after it
                  if (stack.Quantity <= draft.TotalResourceCost + longCost)
                    skipLong = true;
                }
              }

              if (skipLong)
              {
                // Not enough budget for long + trailing — use span-1 pieces instead.
                for (var f = 0; f < cellSpan; f++)
                {
                  canBuildNext = AddPiece(isCorner: false, index: straight, secondaryCount: corners,
                    currentCanBuildNext: canBuildNext);
                  straight++;
                }
              }
              else
              {
                var previousCanBuild = canBuildNext;
                canBuildNext = ZoopPreviewFactory.AddLongStructure(context, longBuildIndex, cellSpan, longIndex,
                  canBuildNext);

                if (canBuildNext)
                {
                  longCounts[cellSpan] = longIndex + 1;
                }
                else
                {
                  // Long piece too expensive — fall back to span-1 pieces for these cells.
                  canBuildNext = previousCanBuild;
                  for (var f = 0; f < cellSpan; f++)
                  {
                    canBuildNext = AddPiece(isCorner: false, index: straight, secondaryCount: corners,
                      currentCanBuildNext: canBuildNext);
                    straight++;
                  }
                }
              }
            }
            else
            {
              canBuildNext = AddPiece(isCorner: false, index: straight, secondaryCount: corners,
                currentCanBuildNext: canBuildNext);
              straight++;
            }

            lastDirection = zoopDirection;
          }
        }
        else
        {
          for (var i = 0; i < straightInDir; i++)
          {
            canBuildNext = AddPiece(isCorner: false, index: straight, secondaryCount: corners,
              currentCanBuildNext: canBuildNext);
            straight++;
            lastDirection = zoopDirection;
          }
        }
      }
    }
  }
}
