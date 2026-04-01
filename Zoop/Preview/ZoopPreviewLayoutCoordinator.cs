using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.EntryPoints.Integrations;
using ZoopMod.Zoop.Planning;

namespace ZoopMod.Zoop.Preview;

internal interface ISmallGridPreviewLayoutAdapter
{
  ZoopDraft Draft { get; }
  Structure GetDraftPreviewStructure(int index);
  int GetDraftCellSpan(int index);
  void ApplyRotation(SmallGridRotationStep step);
  bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure, int structureIndex);
  Vector3Int GetDraftCellKey(Vector3 position);
}

internal interface IBigGridPreviewLayoutAdapter
{
  ZoopDraft Draft { get; }
  Structure GetDraftPreviewStructure(int index);
  bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex);
}

internal struct SmallGridRotationStep
{
  public int StructureCounter { get; set; }
  public bool SupportsCornerVariant { get; set; }
  public bool IsSinglePlacement { get; set; }
  public int SegmentIndex { get; set; }
  public int DirectionIndex { get; set; }
  public int PlacementIndex { get; set; }
  public ZoopDirection LastDirection { get; set; }
  public ZoopDirection ZoopDirection { get; set; }
  public bool IncreasingFrom { get; set; }
  public bool IncreasingTo { get; set; }
}

/// <summary>
/// Positions already-created preview structures onto the planned zoop path.
/// This keeps preview layout concerns separate from path planning and preview instantiation.
/// </summary>
internal static class ZoopPreviewLayoutCoordinator
{
  // Reused across calls to avoid per-frame HashSet allocation. Cleared at the top of every
  // PositionSmallGridStructures call so it never carries stale data from a previous update.
  private static readonly HashSet<Vector3Int> OccupiedCells = new();

  /// <summary>
  /// Walks the planned small-grid segments in build order and assigns each preview piece a world position.
  /// Corner-capable families also get their turn rotation applied here because that depends on neighboring
  /// segment directions rather than on the path plan alone.
  /// </summary>
  public static bool PositionSmallGridStructures(
    ISmallGridPreviewLayoutAdapter adapter,
    InventoryManager inventoryManager,
    List<ZoopSegment> segments,
    bool supportsCornerVariant,
    int spacing,
    bool isSinglePlacement)
  {
    var draft = adapter.Draft;
    var structureCounter = 0;
    var lastDirection = ZoopDirection.none;
    OccupiedCells.Clear();
    var hasError = false;
    var creativeFreedomEnabled = ZoopIntegrations.CreativeFreedomAvailable;

    for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
      float xOffset = 0;
      float yOffset = 0;
      float zOffset = 0;
      var startPos = draft.Waypoints[segmentIndex];
      for (var directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
      {
        if (structureCounter == draft.PreviewCount)
        {
          break;
        }

        var zoopDirection = segment.Directions[directionIndex];
        var increasing = ZoopPathPlanner.GetIncreasingForDirection(zoopDirection, segment);
        var zoopCounter = ZoopPathPlanner.GetPlacementCount(segments.Count, segmentIndex, segment.Directions.Count,
          directionIndex, ZoopPathPlanner.GetCountForDirection(zoopDirection, segment));
        var value = ZoopPathPlanner.GetDirectionalPlacementValue(increasing,
          InventoryManager.ConstructionCursor is SmallGrid, spacing);

        for (var placementIndex = 0; placementIndex < zoopCounter;)
        {
          if (structureCounter == draft.PreviewCount)
          {
            break;
          }

          var cellSpan = adapter.GetDraftCellSpan(structureCounter);

          // Apply rotation first so we can read the actual quaternion to determine
          // which direction the model's mesh extends in world space.
          var increasingFrom = ZoopPathPlanner.GetIncreasingFromPreviousDirection(segments, segment, segmentIndex,
            directionIndex, placementIndex, lastDirection);
          adapter.ApplyRotation(new SmallGridRotationStep
          {
            StructureCounter = structureCounter,
            SupportsCornerVariant = supportsCornerVariant,
            IsSinglePlacement = isSinglePlacement,
            SegmentIndex = segmentIndex,
            DirectionIndex = directionIndex,
            PlacementIndex = placementIndex,
            LastDirection = lastDirection,
            ZoopDirection = zoopDirection,
            IncreasingFrom = increasingFrom,
            IncreasingTo = increasing
          });

          // Each structure family has a different local mesh direction:
          //   Chute: local -X    Pipe: local +Z    Cable: local -Z
          // After rotation, determine if the mesh extends with or against the travel
          // direction. When opposite, place the origin at the far end so the mesh
          // covers the cells behind it.
          var placementOffset = placementIndex;
          if (cellSpan > 1)
          {
            var structure = adapter.GetDraftPreviewStructure(structureCounter);
            var rotation = structure.ThingTransformRotation;
            var axisUnit = zoopDirection == ZoopDirection.x ? Vector3.right
              : zoopDirection == ZoopDirection.y ? Vector3.up
              : Vector3.forward;
            var meshLocalDir = structure is Chute ? Vector3.left
              : structure is Cable ? Vector3.back
              : Vector3.forward;
            var meshAlongTravel = Vector3.Dot(rotation * meshLocalDir, axisUnit);
            if ((meshAlongTravel > 0) != increasing)
              placementOffset = placementIndex + cellSpan - 1;
          }

          var pieceOffset = placementOffset * value;

          ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection,
            pieceOffset);

          lastDirection = zoopDirection;

          var offset = new Vector3(xOffset, yOffset, zOffset);
          var previewPosition = startPos + offset;
          var previewStructure = adapter.GetDraftPreviewStructure(structureCounter);
          previewStructure.GameObject.SetActive(true);
          previewStructure.ThingTransformPosition = previewPosition;
          previewStructure.Position = previewPosition;

          // Track all cells this piece covers and check for overlaps/constructibility.
          hasError = hasError || HasSmallGridCellError(creativeFreedomEnabled, adapter, inventoryManager,
            OccupiedCells, startPos, xOffset, yOffset, zOffset, zoopDirection, value, placementIndex,
            cellSpan, previewStructure, structureCounter);

          structureCounter++;
          placementIndex += cellSpan;
        }

        // Advance the offset past the last piece so the next direction starts at the correct position.
        ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, zoopCounter * value);
      }
    }

    return hasError;
  }

  /// <summary>
  /// Returns true when a cell placement is invalid (occupied or not constructible).
  /// For long pieces (cellSpan > 1), all covered cells are tracked for self-overlap detection.
  /// Always returns false when CreativeFreedom is active, mirroring manual-placement behavior.
  /// </summary>
  private static bool HasSmallGridCellError(
    bool creativeFreedomEnabled,
    ISmallGridPreviewLayoutAdapter adapter,
    InventoryManager inventoryManager,
    HashSet<Vector3Int> occupiedCells,
    Vector3 startPos,
    float xOffset,
    float yOffset,
    float zOffset,
    ZoopDirection zoopDirection,
    float value,
    int placementIndex,
    int cellSpan,
    Structure previewStructure,
    int structureCounter)
  {
    if (creativeFreedomEnabled) return false;

    var hasOverlap = false;
    for (var c = 0; c < cellSpan; c++)
    {
      float cx = xOffset, cy = yOffset, cz = zOffset;
      ZoopPathPlanner.SetDirectionalOffset(ref cx, ref cy, ref cz, zoopDirection, (placementIndex + c) * value);
      var cellPos = startPos + new Vector3(cx, cy, cz);
      var cellKey = adapter.GetDraftCellKey(cellPos);
      if (occupiedCells.Contains(cellKey)) hasOverlap = true;
      occupiedCells.Add(cellKey);
    }

    return hasOverlap ||
           !adapter.CanConstructSmallCell(inventoryManager, previewStructure, structureCounter);
  }

  /// <summary>
  /// Projects the planned large-grid plane into preview positions in row-major order and validates each cell.
  /// Walls and frames do not need the small-grid turn handling, so this flow is just offset calculation plus
  /// per-cell constructibility checks.
  /// </summary>
  public static bool PositionBigGridStructures(
    IBigGridPreviewLayoutAdapter adapter,
    InventoryManager inventoryManager,
    Vector3 startPos,
    ZoopPlane plane,
    int spacing)
  {
    var draft = adapter.Draft;
    var structureCounter = 0;
    var hasError = false;

    if (draft.PreviewCount <= 0)
    {
      return false;
    }

    float xOffset = 0;
    float yOffset = 0;
    float zOffset = 0;

    var safeSpacing = Mathf.Max(spacing, 1);

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      var zoopDirection2 = plane.Directions.direction2;
      var increasing2 = plane.Increasing.direction2;

      var value2 = ZoopPathPlanner.GetDirectionalPlacementValue(increasing2, false, safeSpacing);
      ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection2,
        indexDirection2 * value2);

      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        if (structureCounter == draft.PreviewCount)
        {
          break;
        }

        var zoopDirection1 = plane.Directions.direction1;
        var increasing1 = plane.Increasing.direction1;

        var value1 = ZoopPathPlanner.GetDirectionalPlacementValue(increasing1, false, safeSpacing);
        ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection1,
          indexDirection1 * value1);

        var offset = new Vector3(xOffset, yOffset, zOffset);
        var previewPosition = startPos + offset;
        var previewStructure = adapter.GetDraftPreviewStructure(structureCounter);
        previewStructure.GameObject.SetActive(true);
        previewStructure.ThingTransformPosition = previewPosition;
        previewStructure.Position = previewPosition;
        hasError = hasError || !adapter.CanConstructBigCell(inventoryManager, previewStructure, structureCounter);
        structureCounter++;
      }
    }

    return hasError;
  }
}
