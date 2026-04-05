using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Planning;
using ZoopMod.Zoop.Planning.SmallGrid;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Placement;

/// <summary>
/// Owns the small-grid zoop preview flow from path planning through preview instantiation and placement.
/// </summary>
internal sealed class ZoopSmallGridCoordinator(ZoopPreviewValidator previewValidator) : IZoopGridStrategy
{
  public bool Matches(Structure cursor) => cursor is SmallGrid;
  public bool SupportsWaypoints => true;
  public Vector3? GetCursorPosition(Structure cursor) => cursor.GetLocalGrid().ToVector3();

  private readonly List<ZoopSegment> _segments = [];

  private readonly Dictionary<Structure, List<LongVariant>> _longVariantsByBasePiece = [];
  private readonly Dictionary<int, int> _longCounts = [];

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

      SetStraightRotation(GetAdapterPreviewStructure(structureCounter), step.Segment.Direction);
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
    var rawSegments = BuildSmallGridPlan(draft.Waypoints, currentPos);

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
      out var hasPlacementError);
    draft.HasError = draft.HasError || hasPlacementError;
  }

  /// Half-extent for the Physics overlap check. Small grid cells are 0.5 units apart;
  /// a tiny box at the cell center avoids false positives from adjacent structures.
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

      SplitSegmentAtBarriers(seg, i, segments.Count, barrierCells, result);
    }

    return result;
  }

  // Splits one segment into sub-segments around each barrier cell and appends them to result.
  private static void SplitSegmentAtBarriers(ZoopSegment seg, int segIndex, int totalSegments,
    HashSet<int> barrierCells, List<ZoopSegment> result)
  {
    var zoopCounter = ZoopPathPlanner.GetPlacementCount(segIndex, totalSegments, seg.Length);

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

    var isLastOriginal = segIndex == totalSegments - 1;
    for (var s = 0; s < subCellCounts.Count; s++)
    {
      var cells = subCellCounts[s];
      // Non-last segments use count = cells + 1 so the walker deduplicates the shared
      // endpoint (GetPlacementCount returns count - 1 for non-last). The last sub-segment
      // of the globally last original segment needs count = cells (no deduplication).
      var isLastSub = isLastOriginal && s == subCellCounts.Count - 1;
      var count = isLastSub ? cells : cells + 1;
      var isWaypointStart = s == 0 && seg.IsWaypointStart;
      var isCorner = s == 0 && seg.IsCorner;
      result.Add(new ZoopSegment(seg.Direction, count, seg.Increasing, isWaypointStart, seg.StartPos, isCorner));
    }
  }

  /// <summary>
  /// Builds the flat segment list for the current waypoint path ending at <paramref name="currentPos"/>.
  /// </summary>
  private IReadOnlyList<ZoopSegment> BuildSmallGridPlan(IReadOnlyList<Vector3> waypoints, Vector3 currentPos)
  {
    _segments.Clear();
    var prevDirection = ZoopDirection.none;
    for (var wpIndex = 0; wpIndex < waypoints.Count; wpIndex++)
    {
      var startPos = waypoints[wpIndex];
      var endPos = wpIndex < waypoints.Count - 1 ? waypoints[wpIndex + 1] : currentPos;
      ZoopSegment.AppendBoundarySegments(_segments, startPos, endPos, wpIndex == 0, wpIndex == waypoints.Count - 1,
        prevDirection);
      prevDirection = _segments[_segments.Count - 1].Direction;
    }

    return _segments;
  }

  /// <summary>
  /// Returns true when any world <see cref="SmallGrid"/> collider overlaps the given cell position.
  /// Uses a tiny Physics box so only structures actually at the cell are detected.
  /// </summary>
  private static bool HasSmallGridStructureAt(Vector3 position)
  {
    var colliders = Physics.OverlapBox(position, CellProbeHalfExtent, Quaternion.identity, ~0,
      QueryTriggerInteraction.Ignore);
    return colliders.Any(t => t.GetComponentInParent<SmallGrid>() != null);
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current small-grid path.
  /// When long variants are available (e.g. long-3, long-5, long-10 pipes), consecutive straight
  /// pieces in a direction are packed with the longest fitting variant first.
  /// First piece, last piece, and waypoint pieces are always single-cell.
  /// </summary>
  private void BuildSmallStructureList(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager,
    IReadOnlyList<ZoopSegment> segments,
    bool supportsCornerVariant)
  {
    ZoopPreviewFactory.ResetSmallGridPreviewList(draft, previewCache);
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    var context = new ZoopPreviewContext(draft, previewCache, constructables, inventoryManager, supportsCornerVariant);

    var basePiece = constructables.Count > 0 ? constructables[0] : null;
    if (basePiece == null || !_longVariantsByBasePiece.TryGetValue(basePiece, out var longVariants))
    {
      longVariants = ZoopLongVariantRules.FindLongVariants(constructables);
      if (basePiece != null)
        _longVariantsByBasePiece[basePiece] = longVariants;
    }
    var runPlan = new List<int>();
    _longCounts.Clear();

    var straight = 0;
    var corners = 0;
    var canBuildNext = true;
    ZoopPathPlanner.WalkSmallGridPath(segments, step =>
      ProcessStep(context, longVariants, runPlan, step, ref straight, ref corners, ref canBuildNext));
  }

  private void ProcessStep(ZoopPreviewContext context, List<LongVariant> longVariants, List<int> runPlan,
    ZoopPathStep step, ref int straight, ref int corners, ref bool canBuildNext)
  {
    if (step.Segment.IsCorner && context.SupportsCornerVariant && context.Draft.PreviewCount > 0)
    {
      canBuildNext = ZoopPreviewFactory.AddStructure(context, true, corners, straight, canBuildNext);
      corners++;
      return;
    }

    var straightInDir = step.ZoopCounter;
    if (straightInDir > 0 && longVariants.Count > 0)
    {
      ZoopLongVariantRules.PlanRun(straightInDir, longVariants, runPlan);
      foreach (var cellSpan in runPlan)
      {
        if (cellSpan > 1)
          PlaceLongSpan(context, longVariants,
            cellSpan, corners, ref straight, ref canBuildNext);
        else
          AddStraightCells(context, corners, 1, ref straight, ref canBuildNext);
      }
    }
    else
    {
      AddStraightCells(context, corners, straightInDir, ref straight, ref canBuildNext);
    }
  }

  private static void AddStraightCells(ZoopPreviewContext context, int corners, int count,
    ref int straight, ref bool canBuildNext)
  {
    for (var i = 0; i < count; i++)
    {
      canBuildNext = ZoopPreviewFactory.AddStructure(context, false, straight, corners, canBuildNext);
      straight++;
    }
  }

  // Before placing a long piece, verify the remaining budget covers at least the long piece
  // itself plus one more unit — otherwise fall back to span-1 pieces so the last placed piece
  // is a span-1 variant, keeping the build cursor at the base piece type.
  private void PlaceLongSpan(ZoopPreviewContext context,
    List<LongVariant> longVariants,
    int cellSpan, int corners, ref int straight, ref bool canBuildNext)
  {
    var longBuildIndex = ZoopLongVariantRules.GetBuildIndexForSpan(longVariants, cellSpan);
    if (!_longCounts.TryGetValue(cellSpan, out var longIndex))
      longIndex = 0;

    if (canBuildNext && IsLongSpanBudgetTight(context, longBuildIndex, cellSpan))
    {
      AddStraightCells(context, corners, cellSpan, ref straight, ref canBuildNext);
      return;
    }

    var previousCanBuild = canBuildNext;
    canBuildNext = ZoopPreviewFactory.AddLongStructure(context, longBuildIndex, cellSpan, longIndex, canBuildNext);
    if (canBuildNext)
    {
      _longCounts[cellSpan] = longIndex + 1;
    }
    else
    {
      // Long piece too expensive — fall back to span-1 pieces for these cells.
      canBuildNext = previousCanBuild;
      AddStraightCells(context, corners, cellSpan, ref straight, ref canBuildNext);
    }
  }

  // Returns true when placing this long piece would leave less than one straight piece's worth
  // of budget remaining — i.e. the isolated end cell couldn't be afforded after the long.
  private static bool IsLongSpanBudgetTight(ZoopPreviewContext context, int longBuildIndex, int cellSpan)
  {
    if (InventoryManager.ActiveHandSlot.Get() is not Stackable stack) return false;
    var constructables = context.Constructables;
    var longCost = longBuildIndex >= 0 && longBuildIndex < constructables.Count
      ? ZoopPreviewFactory.GetEntryQuantity(constructables[longBuildIndex])
      : cellSpan;
    var straightCost = constructables.Count > 0
      ? ZoopPreviewFactory.GetEntryQuantity(constructables[0])
      : 1;
    return stack.Quantity < context.Draft.TotalResourceCost + longCost + straightCost;
  }
}
