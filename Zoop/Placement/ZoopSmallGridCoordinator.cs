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

    public void ApplyRotation(
      int structureCounter,
      ZoopPathStep step,
      int placementIndex,
      ZoopDirection lastDirection,
      bool increasingFrom,
      bool isSinglePlacement,
      bool supportsCornerVariant)
    {
      if (!supportsCornerVariant)
      {
        return;
      }

      var isSegmentTurnStart = !step.IsGlobalFirst && placementIndex == 0;
      if (isSegmentTurnStart)
      {
        if (lastDirection == step.Segment.Direction)
        {
          SetStraightRotation(GetAdapterPreviewStructure(structureCounter), step.Segment.Direction);
        }
        else
        {
          SetCornerRotation(GetAdapterPreviewStructure(structureCounter), lastDirection, increasingFrom,
            step.Segment.Direction, step.Segment.Increasing);
        }

        return;
      }

      if (!isSinglePlacement)
      {
        SetStraightRotation(GetAdapterPreviewStructure(structureCounter), step.Segment.Direction);
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
    Vector3 currentPos)
  {
    var rawSegments = ZoopPathPlanner.BuildSmallGridPlan(draft.Waypoints, currentPos);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    // Pre-scan every cell along the path for existing structures. Occupied cells
    // become single-cell segments in the path so long variants never straddle an obstacle.
    var segments = ScanAndSplitAtBarriers(rawSegments);

    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    var supportsCornerVariant =
      ZoopConstructableRules.SupportsCornerVariant(constructables,
        inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);

    BuildSmallStructureList(draft, previewCache, inventoryManager, segments, supportsCornerVariant);

    if (draft.PreviewCount <= 0)
    {
      return;
    }

    layoutAdapter.Draft = draft;
    ZoopPreviewLayoutCoordinator.PositionSmallGridStructures(
      layoutAdapter,
      inventoryManager,
      segments,
      supportsCornerVariant,
      isSinglePlacement: segments.Count == 0,
      out var hasPlacementError);
    draft.HasError = draft.HasError || hasPlacementError;
  }

  // Half-extent for the Physics overlap check. Small grid cells are 0.5 units apart;
  // a tiny box at the cell center avoids false positives from adjacent structures.
  private static readonly Vector3 CellProbeHalfExtent = Vector3.one * 0.05f;

  /// <summary>
  /// Walks every cell position along the planned path using a Physics overlap check to find
  /// existing small-grid structures, then splits each affected segment so that each occupied
  /// cell becomes its own 1-cell sub-segment. Long-piece planning will never span a sub-segment
  /// boundary, so occupied cells are naturally forced to span-1 without a separate barrier pass.
  /// Preview structures have their colliders disabled and the construction cursor is deactivated,
  /// so the overlap only finds actual world structures.
  /// </summary>
  private static IReadOnlyList<ZoopSegment> ScanAndSplitAtBarriers(IReadOnlyList<ZoopSegment> segments)
  {
    if (InventoryManager.ConstructionCursor == null) return segments;

    Dictionary<int, HashSet<int>> occupied = null;

    ZoopPathPlanner.WalkSmallGridPath(segments, step =>
    {
      for (var cellIndex = 0; cellIndex < step.ZoopCounter; cellIndex++)
      {
        if (!HasSmallGridStructureAt(step.GetCellPosition(cellIndex)))
          continue;

        occupied ??= [];
        if (!occupied.TryGetValue(step.SegmentIndex, out var cellSet))
        {
          cellSet = [];
          occupied[step.SegmentIndex] = cellSet;
        }

        cellSet.Add(cellIndex);
      }
    });

    return occupied == null ? segments : SplitAtBarriers(segments, occupied);
  }

  /// <summary>
  /// Returns a new segment list where each cell index listed in <paramref name="barriers"/> has been
  /// extracted into its own 1-cell <see cref="ZoopSegment"/>.
  /// The surrounding cells become their own sub-segments sharing the same direction and start
  /// position as the original. The walker's offset accumulation stays correct because sub-segments
  /// within the same waypoint span are not marked as waypoint starts (except when the original was).
  /// </summary>
  private static IReadOnlyList<ZoopSegment> SplitAtBarriers(
    IReadOnlyList<ZoopSegment> segments,
    Dictionary<int, HashSet<int>> barriers)
  {
    var result = new List<ZoopSegment>(segments.Count);

    for (var i = 0; i < segments.Count; i++)
    {
      var seg = segments[i];
      if (!barriers.TryGetValue(i, out var barrierCells))
      {
        result.Add(seg);
        continue;
      }

      var zoopCounter = ZoopPathPlanner.GetPlacementCount(i, segments.Count, seg.Count);

      var sorted = new List<int>(barrierCells);
      sorted.Sort();

      // Collect cell-counts for each sub-segment in order.
      var subCellCounts = new List<int>();
      var prevStart = 0;
      foreach (var b in sorted)
      {
        if (b >= zoopCounter) break;
        if (b > prevStart)
          subCellCounts.Add(b - prevStart);
        subCellCounts.Add(1); // isolated barrier cell
        prevStart = b + 1;
      }
      if (prevStart < zoopCounter)
        subCellCounts.Add(zoopCounter - prevStart);

      var isLastOriginal = i == segments.Count - 1;
      for (var s = 0; s < subCellCounts.Count; s++)
      {
        var cells = subCellCounts[s];
        // Non-last segments use count = cells + 1 so the walker deduplicates the shared
        // endpoint (GetPlacementCount returns count - 1 for non-last). The last sub-segment
        // of the globally last original segment needs count = cells (no deduplication).
        var isLastSub = isLastOriginal && s == subCellCounts.Count - 1;
        var count = isLastSub ? cells : cells + 1;
        var isWaypointStart = s == 0 && seg.IsWaypointStart;
        result.Add(new ZoopSegment(seg.Direction, count, seg.Increasing, isWaypointStart, seg.StartPos));
      }
    }

    return result;
  }

  /// <summary>
  /// Returns true when any world <see cref="SmallGrid"/> collider overlaps the given cell position.
  /// Uses a tiny Physics box so only structures actually at the cell are detected.
  /// </summary>
  private static bool HasSmallGridStructureAt(Vector3 position)
  {
    var colliders = Physics.OverlapBox(position, CellProbeHalfExtent, Quaternion.identity, ~0,
      QueryTriggerInteraction.Ignore);
    for (var i = 0; i < colliders.Length; i++)
    {
      if (colliders[i].GetComponentInParent<SmallGrid>() != null)
        return true;
    }

    return false;
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current small-grid path.
  /// When long variants are available (e.g. long-3, long-5, long-10 pipes), consecutive straight
  /// pieces in a direction are packed with the longest fitting variant first.
  /// First piece, last piece, and waypoint pieces are always single-cell.
  /// </summary>
  private static void BuildSmallStructureList(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager,
    IReadOnlyList<ZoopSegment> segments,
    bool supportsCornerVariant)
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
    var canBuildNext = true;
    ZoopPathPlanner.WalkSmallGridPath(segments, step =>
    {
      if (step.Segment.IsCorner && supportsCornerVariant && draft.PreviewCount > 0)
      {
        canBuildNext = AddPiece(isCorner: true, index: corners, secondaryCount: straight,
          currentCanBuildNext: canBuildNext);
        corners++;
        return;
      }

      var straightInDir = step.ZoopCounter;

      if (straightInDir > 0 && hasLongVariants)
      {
        ZoopLongVariantRules.PlanRun(straightInDir, longVariants, runPlan);

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
          }
      }
      else
      {
        for (var i = 0; i < straightInDir; i++)
        {
          canBuildNext = AddPiece(isCorner: false, index: straight, secondaryCount: corners,
            currentCanBuildNext: canBuildNext);
          straight++;
        }
      }
    });
  }
}
