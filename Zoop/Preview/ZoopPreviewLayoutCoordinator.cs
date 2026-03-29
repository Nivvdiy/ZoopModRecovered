using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.EntryPoints.Integrations;
using ZoopMod.Zoop.Planning;

namespace ZoopMod.Zoop.Preview;

internal interface ISmallGridPreviewLayoutAdapter
{
  ZoopDraft Draft { get; }
  Structure GetDraftPreviewStructure(int index);
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

internal sealed class SmallGridRotationStep
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
    var occupiedCells = new HashSet<Vector3Int>();
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

        for (var placementIndex = 0; placementIndex < zoopCounter; placementIndex++)
        {
          if (structureCounter == draft.PreviewCount)
          {
            break;
          }

          ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection,
            placementIndex * value);

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

          lastDirection = zoopDirection;

          var offset = new Vector3(xOffset, yOffset, zOffset);
          var previewPosition = startPos + offset;
          var previewStructure = adapter.GetDraftPreviewStructure(structureCounter);
          previewStructure.GameObject.SetActive(true);
          previewStructure.ThingTransformPosition = previewPosition;
          previewStructure.Position = previewPosition;
          if (!creativeFreedomEnabled)
          {
            // Small-grid previews cannot safely overlap the same snapped cell because there is no dedicated
            // intersection preview for most families, so revisiting a cell is treated as invalid.
            var cellKey = adapter.GetDraftCellKey(previewPosition);
            var revisitsExistingZoopCell = occupiedCells.Contains(cellKey);
            occupiedCells.Add(cellKey);
            hasError = hasError || revisitsExistingZoopCell;
            hasError = hasError || !adapter.CanConstructSmallCell(inventoryManager, previewStructure, structureCounter);
          }

          structureCounter++;
          if (placementIndex == zoopCounter - 1)
          {
            ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection,
              (placementIndex + 1) * value);
          }
        }
      }
    }

    return hasError;
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
