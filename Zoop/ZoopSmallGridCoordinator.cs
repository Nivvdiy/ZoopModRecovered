using System;
using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZoopMod.Zoop;

/// <summary>
/// Owns the small-grid zoop preview flow from path planning through preview instantiation and placement.
/// </summary>
internal sealed class ZoopSmallGridCoordinator(
  ZoopSession session,
  ZoopPreviewFactory previewFactory,
  Func<InventoryManager, Structure, int, bool> canConstructSmallCell,
  Action<bool> setHasError)
{
  /// <summary>
  /// Rebuilds the active small-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(InventoryManager inventoryManager, Vector3 currentPos, List<ZoopSegment> segments,
    int spacing)
  {
    var plan = ZoopPathPlanner.BuildSmallGridPlan(session.Waypoints, currentPos);
    segments.Clear();
    segments.AddRange(plan.Segments);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    var supportsCornerVariant =
      ZoopConstructableRules.SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables,
        inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
    BuildSmallStructureList(inventoryManager, segments, supportsCornerVariant);

    if (session.PreviewCount <= 0)
    {
      return;
    }

    ZoopPreviewLayoutCoordinator.PositionSmallGridStructures(
      session,
      inventoryManager,
      segments,
      supportsCornerVariant,
      plan.IsSinglePlacement,
      spacing,
      GetPreviewStructure,
      ApplySmallGridRotation,
      canConstructSmallCell,
      GetSmallGridCellKey,
      ZoopMod.CFree,
      setHasError);
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current small-grid path.
  /// </summary>
  private void BuildSmallStructureList(InventoryManager inventoryManager, List<ZoopSegment> segments,
    bool supportsCornerVariant)
  {
    previewFactory.ResetSmallGridPreviewList();

    var straight = 0;
    var corners = 0;
    var lastDirection = ZoopDirection.none;
    var canBuildNext = true;
    for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
      for (var directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
      {
        var zoopDirection = segment.Directions[directionIndex];
        var zoopCounter = ZoopPathPlanner.GetCountForDirection(zoopDirection, segment);

        zoopCounter = ZoopPathPlanner.GetPlacementCount(segments.Count, segmentIndex, segment.Directions.Count,
          directionIndex, zoopCounter);

        for (var placementIndex = 0; placementIndex < zoopCounter; placementIndex++)
        {
          if (session.PreviewCount > 0 && (placementIndex == 0 || segmentIndex > 0) && supportsCornerVariant)
          {
            if (zoopDirection != lastDirection)
            {
              previewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, true, corners,
                straight, ref canBuildNext, inventoryManager,
                supportsCornerVariant); // start with corner on secondary and tertiary zoop directions
              corners++;
            }
            else
            {
              previewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight,
                corners, ref canBuildNext, inventoryManager, supportsCornerVariant);
              straight++;
            }
          }
          else
          {
            previewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight,
              corners, ref canBuildNext, inventoryManager, supportsCornerVariant);
            straight++;
          }

          lastDirection = zoopDirection;
        }
      }
    }
  }

  /// <summary>
  /// Applies the correct rotation to a small-grid preview structure based on whether it is straight or a turn.
  /// </summary>
  private void ApplySmallGridRotation(int structureCounter, bool supportsCornerVariant, bool singleItem,
    int segmentIndex, int directionIndex, int placementIndex, ZoopDirection lastDirection, ZoopDirection zoopDirection,
    bool increasingFrom, bool increasingTo)
  {
    if (!supportsCornerVariant)
    {
      return;
    }

    var isSegmentTurnStart = (directionIndex > 0 || (segmentIndex > 0 && directionIndex == 0)) && placementIndex == 0;
    if (isSegmentTurnStart)
    {
      if (lastDirection == zoopDirection)
      {
        SetStraightRotationSmallGrid(GetPreviewStructure(structureCounter), zoopDirection);
      }
      else
      {
        SetCornerRotation(GetPreviewStructure(structureCounter), lastDirection, increasingFrom, zoopDirection,
          increasingTo);
      }

      return;
    }

    if (!singleItem)
    {
      SetStraightRotationSmallGrid(GetPreviewStructure(structureCounter), zoopDirection);
    }
  }

  /// <summary>
  /// Converts a small-grid position into a stable half-grid cell key.
  /// </summary>
  private static Vector3Int GetSmallGridCellKey(Vector3 position)
  {
    return new Vector3Int(
      Mathf.RoundToInt(position.x * 2f),
      Mathf.RoundToInt(position.y * 2f),
      Mathf.RoundToInt(position.z * 2f));
  }

  /// <summary>
  /// Rotates a small-grid straight preview to match its zoop direction.
  /// </summary>
  private static void SetStraightRotationSmallGrid(Structure structure, ZoopDirection zoopDirection)
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

  /// <summary>
  /// Rotates a corner preview so it connects the previous and next zoop directions.
  /// </summary>
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

  /// <summary>
  /// Applies a rotation to both the structure state and Unity transform.
  /// </summary>
  private static void SetStructureRotation(Structure structure, Quaternion rotation)
  {
    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
  }

  private Structure GetPreviewStructure(int index)
  {
    return session.PreviewPieces[index].Structure;
  }
}
