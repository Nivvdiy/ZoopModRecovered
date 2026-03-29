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
  ZoopSession session,
  ZoopPreviewFactory previewFactory,
  ZoopPreviewValidator previewValidator)
{
  /// <summary>
  /// Rebuilds the active large-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(InventoryManager inventoryManager, Vector3 currentPos, int spacing)
  {
    var startPos = session.Waypoints[0];
    var endPos = ClampWallZoopPositionToStartPlane(startPos, currentPos);

    var plane = ZoopPathPlanner.BuildBigGridPlane(startPos, endPos);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    BuildBigStructureList(inventoryManager, plane);

    if (session.PreviewCount <= 0)
    {
      return;
    }

    ZoopPreviewLayoutCoordinator.PositionBigGridStructures(
      session,
      inventoryManager,
      startPos,
      plane,
      spacing,
      GetPreviewStructure,
      CanConstructBigCell,
      hasError => session.HasError = session.HasError || hasError);
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current large-grid plane.
  /// </summary>
  private void BuildBigStructureList(InventoryManager inventoryManager, ZoopPlane plane)
  {
    previewFactory.ResetBigGridPreviewList();
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        previewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0,
          ref canBuildNext, inventoryManager, false);
        count++;
      }
    }
  }

  private Structure GetPreviewStructure(int index)
  {
    return session.PreviewPieces[index].Structure;
  }

  private bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return previewValidator.CanConstructBigCell(session, inventoryManager, structure, structureIndex);
  }

  private Vector3 ClampWallZoopPositionToStartPlane(Vector3 startPos, Vector3 targetPos)
  {
    if (InventoryManager.ConstructionCursor is not Wall || session.ZoopStartWallNormal == Vector3.zero)
    {
      return targetPos;
    }

    if (Mathf.Abs(session.ZoopStartWallNormal.x) > 0.99f)
    {
      targetPos.x = startPos.x;
    }
    else if (Mathf.Abs(session.ZoopStartWallNormal.y) > 0.99f)
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
