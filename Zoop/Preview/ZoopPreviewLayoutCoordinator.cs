using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.EntryPoints.Integrations;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Planning;
using ZoopMod.Zoop.Planning.SmallGrid;

namespace ZoopMod.Zoop.Preview;

internal interface ISmallGridPreviewLayoutAdapter
{
  ZoopDraft Draft { get; }
  Structure GetDraftPreviewStructure(int index);
  int GetDraftCellSpan(int index);
  void ApplyRotation(
    int structureCounter,
    ZoopPathStep step,
    int placementIndex,
    ZoopDirection lastDirection,
    bool increasingFrom,
    bool supportsCornerVariant);
  bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure, int structureIndex);
  Vector3Int GetDraftCellKey(Vector3 position);
}

internal interface IBigGridPreviewLayoutAdapter
{
  ZoopDraft Draft { get; }
  Structure GetDraftPreviewStructure(int index);
  bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex);
}

/// <summary>
/// Positions already-created preview structures onto the planned zoop path.
/// This keeps preview layout concerns separate from path planning and preview instantiation.
/// </summary>
internal static class ZoopPreviewLayoutCoordinator
{
  private const float BigGridCellSpacing = 2f;

  // Reused across calls to avoid per-frame HashSet allocation. Cleared at the top of every
  // PositionSmallGridStructures call so it never carries stale data from a previous update.
  private static readonly HashSet<Vector3Int> OccupiedCells = new();

  /// <summary>
  /// Walks the planned small-grid segments in build order and assigns each preview piece a world position.
  /// Corner-capable families also get their turn rotation applied here because that depends on neighboring
  /// segment directions rather than on the path plan alone.
  /// </summary>
  public static void PositionSmallGridStructures(
    ISmallGridPreviewLayoutAdapter adapter,
    InventoryManager inventoryManager,
    IReadOnlyList<ZoopSegment> segments,
    bool supportsCornerVariant,
    out bool hasPlacementError)
  {
    var draft = adapter.Draft;
    var structureCounter = 0;
    var lastDirection = ZoopDirection.none;
    OccupiedCells.Clear();
    var hasError = false;

    ZoopPathPlanner.WalkSmallGridPath(segments, step =>
    {
      if (structureCounter == draft.PreviewCount) return;

      var increasing = step.Segment.Increasing;

      for (var placementIndex = 0; placementIndex < step.ZoopCounter;)
      {
        if (structureCounter == draft.PreviewCount) break;

          var cellSpan = adapter.GetDraftCellSpan(structureCounter);

          // Apply rotation first so we can read the actual quaternion to determine
          // which direction the model's mesh extends in world space.
          // increasingFromPrevious is precomputed by the walker; no segment lookback needed here.
          adapter.ApplyRotation(structureCounter, step, placementIndex, lastDirection, step.IncreasingFromPrevious, supportsCornerVariant);

          // Each structure family has a different local mesh direction:
          //   Chute: local -X    Pipe: local +Z    Cable: local -Z
          // After rotation, determine if the mesh extends with or against the travel
          // direction. When opposite, place the origin at the far end so the mesh
          // covers the cells behind it.
          var placementOffset = placementIndex;
          if (cellSpan > 1)
          {
            var structure = adapter.GetDraftPreviewStructure(structureCounter);
            placementOffset = ComputePlacementOffset(step, structure, increasing, placementIndex, cellSpan);
          }

          var pieceOffset = placementOffset * step.CellStride;
          var baseDir = ZoopPathPlanner.GetDirectionalComponent(step.BaseOffset, step.Segment.Direction);

          lastDirection = step.Segment.Direction;

          var previewPosition = step.Segment.StartPos +
            ZoopPathPlanner.WithDirectionalOffset(step.BaseOffset, step.Segment.Direction, baseDir + pieceOffset);
          var previewStructure = adapter.GetDraftPreviewStructure(structureCounter);
          previewStructure.GameObject.SetActive(true);
          previewStructure.ThingTransformPosition = previewPosition;
          previewStructure.Position = previewPosition;

          // Cell tracking always starts from the near end (placementIndex), regardless of
          // which end the mesh origin is placed at.
          var cellOffset = ZoopPathPlanner.WithDirectionalOffset(step.BaseOffset, step.Segment.Direction,
            baseDir + placementIndex * step.CellStride);

          // Track all cells this piece covers and check for overlaps/constructibility.
          var cellError = HasSmallGridCellError(adapter, inventoryManager,
            step, cellOffset, cellSpan, previewStructure, structureCounter);
          hasError = hasError || cellError;

          if (cellError)
          {
            ZoopLog.Debug($"[CellError] segment={step.SegmentIndex} pIdx={placementIndex} span={cellSpan}");
          }

          structureCounter++;
          placementIndex += cellSpan;
      }
    });
    hasPlacementError = hasError;
  }

  /// <summary>
  /// Returns true when a cell placement is invalid (occupied or not constructible).
  /// For long pieces (cellSpan > 1), all covered cells are tracked for self-overlap detection.
  /// Always returns false when CreativeFreedom is active, mirroring manual-placement behavior.
  /// </summary>
  private static bool HasSmallGridCellError(
    ISmallGridPreviewLayoutAdapter adapter,
    InventoryManager inventoryManager,
    ZoopPathStep step,
    Vector3 cellOffset,
    int cellSpan,
    Structure previewStructure,
    int structureCounter)
  {
    if (ZoopIntegrations.CreativeFreedomAvailable) return false;

    var dirBase = ZoopPathPlanner.GetDirectionalComponent(cellOffset, step.Segment.Direction);
    var cx = cellOffset.x;
    var cy = cellOffset.y;
    var cz = cellOffset.z;
    var hasOverlap = false;
    for (var c = 0; c < cellSpan; c++)
    {
      ZoopPathPlanner.SetDirectionalOffset(ref cx, ref cy, ref cz, step.Segment.Direction, dirBase + c * step.CellStride);
      var cellPos = step.Segment.StartPos + new Vector3(cx, cy, cz);
      var cellKey = adapter.GetDraftCellKey(cellPos);
      if (OccupiedCells.Contains(cellKey)) hasOverlap = true;
      OccupiedCells.Add(cellKey);
    }

    return hasOverlap ||
           !adapter.CanConstructSmallCell(inventoryManager, previewStructure, structureCounter);
  }

  // For a long piece, determines whether the mesh origin should be placed at the far end
  // (placementIndex + cellSpan - 1) rather than the near end, based on the structure's
  // local mesh direction relative to the travel direction after rotation.
  private static int ComputePlacementOffset(ZoopPathStep step, Structure structure,
    bool increasing, int placementIndex, int cellSpan)
  {
    var axisUnit = step.Segment.Direction switch
    {
      ZoopDirection.x => Vector3.right,
      ZoopDirection.y => Vector3.up,
      _ => Vector3.forward
    };
    var meshLocalDir = structure switch
    {
      Chute => Vector3.left,
      Cable => Vector3.back,
      _ => Vector3.forward
    };
    var meshAlongTravel = Vector3.Dot(structure.ThingTransformRotation * meshLocalDir, axisUnit);
    return (meshAlongTravel > 0) != increasing
      ? placementIndex + cellSpan - 1
      : placementIndex;
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
    ZoopPlane plane)
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

    var zoopDirection1 = plane.Directions.direction1;
    var zoopDirection2 = plane.Directions.direction2;
    var value1 = plane.Increasing.direction1 ? BigGridCellSpacing : -BigGridCellSpacing;
    var value2 = plane.Increasing.direction2 ? BigGridCellSpacing : -BigGridCellSpacing;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection2,
        indexDirection2 * value2);

      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        if (structureCounter == draft.PreviewCount)
        {
          break;
        }

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
