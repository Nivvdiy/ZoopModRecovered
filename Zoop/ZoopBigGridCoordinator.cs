using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZoopMod.Zoop;

/// <summary>
/// Owns the large-grid zoop preview flow from wall-plane planning through preview placement.
/// </summary>
internal sealed class ZoopBigGridCoordinator(
  ZoopSession session,
  ZoopPreviewFactory previewFactory,
  Func<InventoryManager, Structure, int, bool> canConstructBigCell,
  Action<bool> setHasError)
{
  /// <summary>
  /// Rebuilds the active large-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(InventoryManager inventoryManager, Vector3 currentPos, int spacing,
    Func<Vector3, Vector3, Vector3> clampWallZoopPositionToStartPlane)
  {
    var startPos = session.Waypoints[0];
    var endPos = clampWallZoopPositionToStartPlane(startPos, currentPos);

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
      canConstructBigCell,
      setHasError);
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
}
