using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Planning;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Placement;

/// <summary>
/// Owns the large-grid zoop preview flow from wall-plane planning through preview placement.
/// </summary>
internal sealed class ZoopBigGridCoordinator(
  ZoopPreviewFactory previewFactory,
  ZoopPreviewValidator previewValidator)
{
  /// <summary>
  /// Rebuilds the active large-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    Vector3 currentPos, int spacing)
  {
    var startPos = draft.Waypoints[0];
    var endPos = ClampWallZoopPositionToStartPlane(draft, startPos, currentPos);

    var plane = ZoopPathPlanner.BuildBigGridPlane(startPos, endPos);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    BuildBigStructureList(draft, previewCache, inventoryManager, plane);

    if (draft.PreviewCount <= 0)
    {
      return;
    }

    ZoopPreviewLayoutCoordinator.PositionBigGridStructures(
      draft,
      inventoryManager,
      startPos,
      plane,
      spacing,
      index => GetPreviewStructure(draft, index),
      (manager, structure, structureIndex) => CanConstructBigCell(draft, manager, structure, structureIndex),
      hasError => draft.HasError = draft.HasError || hasError);
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current large-grid plane.
  /// </summary>
  private void BuildBigStructureList(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    ZoopPlane plane)
  {
    previewFactory.ResetBigGridPreviewList(draft, previewCache);
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        previewFactory.AddStructure(draft, previewCache, inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0,
          ref canBuildNext, inventoryManager, false);
        count++;
      }
    }
  }

  private static Structure GetPreviewStructure(ZoopDraft draft, int index)
  {
    return draft.PreviewPieces[index].Structure;
  }

  private bool CanConstructBigCell(ZoopDraft draft, InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return previewValidator.CanConstructBigCell(draft, inventoryManager, structure, structureIndex);
  }

  private static Vector3 ClampWallZoopPositionToStartPlane(ZoopDraft draft, Vector3 startPos, Vector3 targetPos)
  {
    if (InventoryManager.ConstructionCursor is not Wall || draft.ZoopStartWallNormal == Vector3.zero)
    {
      return targetPos;
    }

    if (Mathf.Abs(draft.ZoopStartWallNormal.x) > 0.99f)
    {
      targetPos.x = startPos.x;
    }
    else if (Mathf.Abs(draft.ZoopStartWallNormal.y) > 0.99f)
    {
      targetPos.y = startPos.y;
    }
    else
    {
      targetPos.z = startPos.z;
    }

    return targetPos;
  }
}
