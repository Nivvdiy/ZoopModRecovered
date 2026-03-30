using System;
using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
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
  private sealed class SmallGridPreviewLayoutAdapter(ZoopDraft draft, ZoopPreviewValidator previewValidator)
    : ISmallGridPreviewLayoutAdapter
  {
    public ZoopDraft Draft { get; } = draft;

    public Structure GetDraftPreviewStructure(int index)
    {
      return Draft.PreviewPieces[index].Structure;
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
      return ToSmallGridCellKey(position);
    }

    private Structure GetAdapterPreviewStructure(int index)
    {
      return Draft.PreviewPieces[index].Structure;
    }

    private static Vector3Int ToSmallGridCellKey(Vector3 position)
    {
      return new Vector3Int(
        Mathf.RoundToInt(position.x * 2f),
        Mathf.RoundToInt(position.y * 2f),
        Mathf.RoundToInt(position.z * 2f));
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

  /// <summary>
  /// Rebuilds the active small-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    Vector3 currentPos, List<ZoopSegment> segments, int spacing)
  {
    var plan = ZoopPathPlanner.BuildSmallGridPlan(draft.Waypoints, currentPos);
    segments.Clear();
    segments.AddRange(plan.Segments);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    var supportsCornerVariant =
      ZoopConstructableRules.SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables,
        inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
    BuildSmallStructureList(draft, previewCache, inventoryManager, segments, supportsCornerVariant);

    if (draft.PreviewCount <= 0)
    {
      return;
    }

    var layoutAdapter = new SmallGridPreviewLayoutAdapter(draft, previewValidator);
    draft.HasError = draft.HasError || ZoopPreviewLayoutCoordinator.PositionSmallGridStructures(
      layoutAdapter,
      inventoryManager,
      segments,
      supportsCornerVariant,
      spacing,
      plan.IsSinglePlacement);
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current small-grid path.
  /// </summary>
  private static void BuildSmallStructureList(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager,
    List<ZoopSegment> segments,
    bool supportsCornerVariant)
  {
    ZoopPreviewFactory.ResetSmallGridPreviewList(draft, previewCache);
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;

    // Pre-populate the fields that are invariant across every piece in this call.
    var requestTemplate = new ZoopPreviewFactory.AddStructureRequest
    {
      Draft = draft,
      PreviewCache = previewCache,
      Constructables = constructables,
      SupportsCornerVariant = supportsCornerVariant,
      InventoryManager = inventoryManager
    };

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
          // A corner turn piece is needed at the start of any new direction — but only when there
          // are already previewed pieces to connect to and the constructable supports corner variants.
          var isCornerTurn = supportsCornerVariant &&
                             draft.PreviewCount > 0 &&
                             (placementIndex == 0 || segmentIndex > 0) &&
                             zoopDirection != lastDirection;

          if (isCornerTurn)
          {
            // Place a corner piece to bridge from the previous direction to the new one.
            AddPiece(requestTemplate, isCorner: true, index: corners, secondaryCount: straight, ref canBuildNext);
            corners++;
          }
          else
          {
            AddPiece(requestTemplate, isCorner: false, index: straight, secondaryCount: corners, ref canBuildNext);
            straight++;
          }

          lastDirection = zoopDirection;
        }
      }
    }
  }

  private static void AddPiece(
    ZoopPreviewFactory.AddStructureRequest request,
    bool isCorner,
    int index,
    int secondaryCount,
    ref bool canBuildNext)
  {
    request.IsCorner = isCorner;
    request.Index = index;
    request.SecondaryCount = secondaryCount;
    request.CanBuildNext = canBuildNext;
    ZoopPreviewFactory.AddStructure(request);
    canBuildNext = request.CanBuildNext;
  }
}
