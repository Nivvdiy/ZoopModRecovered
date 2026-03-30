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
internal sealed class ZoopBigGridCoordinator(ZoopPreviewValidator previewValidator)
{
  private sealed class BigGridPreviewLayoutAdapter(ZoopDraft draft, ZoopPreviewValidator previewValidator)
    : IBigGridPreviewLayoutAdapter
  {
    public ZoopDraft Draft { get; } = draft;

    public Structure GetDraftPreviewStructure(int index)
    {
      return Draft.PreviewPieces[index].Structure;
    }

    public bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
    {
      return previewValidator.CanConstructBigCell(Draft, inventoryManager, structure, structureIndex);
    }
  }

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

    var layoutAdapter = new BigGridPreviewLayoutAdapter(draft, previewValidator);
    draft.HasError = draft.HasError || ZoopPreviewLayoutCoordinator.PositionBigGridStructures(
      layoutAdapter,
      inventoryManager,
      startPos,
      plane,
      spacing);
  }

  /// <summary>
  /// Creates or reuses the preview pieces needed for the current large-grid plane.
  /// </summary>
  private static void BuildBigStructureList(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager,
    ZoopPlane plane)
  {
    ZoopPreviewFactory.ResetBigGridPreviewList(draft, previewCache);
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        var request = new ZoopPreviewFactory.AddStructureRequest
        {
          Draft = draft,
          PreviewCache = previewCache,
          Constructables = constructables,
          SupportsCornerVariant = false,
          InventoryManager = inventoryManager,
          IsCorner = false,
          Index = count,
          SecondaryCount = 0,
          CanBuildNext = canBuildNext
        };
        ZoopPreviewFactory.AddStructure(request);
        canBuildNext = request.CanBuildNext;
        count++;
      }
    }
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
