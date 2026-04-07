using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Planning;
using ZoopMod.Zoop.Planning.BigGrid;

namespace ZoopMod.Zoop.Preview;

/// <summary>
/// Owns the large-grid zoop preview flow from wall-plane planning through preview placement.
/// </summary>
internal sealed class ZoopBigGridPreviewStrategy(ZoopPreviewValidator previewValidator) : IZoopPreviewGridStrategy
{
  public bool Matches(Structure cursor) => cursor is LargeStructure;
  public bool SupportsWaypoints => false;
  public Vector3? GetCursorPosition(Structure cursor) => cursor.ThingTransformPosition;

  private sealed class BigGridPreviewLayoutAdapter(ZoopPreviewValidator previewValidator)
    : IBigGridPreviewLayoutAdapter
  {
    public ZoopDraft Draft { get; set; }

    public Structure GetDraftPreviewStructure(int index)
    {
      return Draft.PreviewPieces[index].Structure;
    }

    public bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
    {
      return previewValidator.CanConstructBigCell(Draft, inventoryManager, structure, structureIndex);
    }
  }

  private readonly BigGridPreviewLayoutAdapter layoutAdapter = new(previewValidator);

  /// <summary>
  /// Rebuilds the active large-grid preview for the current snapped cursor position.
  /// </summary>
  public async UniTask UpdatePreview(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    Vector3 currentPos)
  {
    var startPos = draft.Waypoints[0];
    var endPos = ClampWallZoopPositionToStartPlane(draft, startPos, currentPos);

    var plane = BuildBigGridPlane(startPos, endPos);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    BuildBigStructureList(draft, previewCache, inventoryManager, plane);

    if (draft.PreviewCount <= 0)
    {
      return;
    }

    layoutAdapter.Draft = draft;
    draft.HasError = draft.HasError || ZoopPreviewLayoutCoordinator.PositionBigGridStructures(
      layoutAdapter,
      inventoryManager,
      startPos,
      plane);
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
    var context = new ZoopPreviewContext(draft, previewCache, constructables, inventoryManager, supportsCornerVariant: false);
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        canBuildNext = ZoopPreviewFactory.AddStructure(context, isCorner: false, index: count, secondaryCount: 0, canBuildNext: canBuildNext);
        count++;
      }
    }
  }

  private static Vector3 ClampWallZoopPositionToStartPlane(ZoopDraft draft, Vector3 startPos, Vector3 targetPos)
  {
    if (InventoryManager.ConstructionCursor is not Wall || draft.Session.StartWallNormal == Vector3.zero)
    {
      return targetPos;
    }

    if (Mathf.Abs(draft.Session.StartWallNormal.x) > 0.99f)
    {
      targetPos.x = startPos.x;
    }
    else if (Mathf.Abs(draft.Session.StartWallNormal.y) > 0.99f)
    {
      targetPos.y = startPos.y;
    }
    else
    {
      targetPos.z = startPos.z;
    }

    return targetPos;
  }

  private static ZoopPlane BuildBigGridPlane(Vector3 startPos, Vector3 endPos)
  {
    var plane = new ZoopPlane();
    CalculateZoopPlane(startPos, endPos, plane);

    if (!ZoopPositionUtility.IsSameZoopPosition(startPos, endPos))
    {
      return plane;
    }

    plane.Count = (direction1: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2),
      direction2: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2));
    plane.Increasing = (direction1: startPos.x < endPos.x, direction2: startPos.y < endPos.y);
    plane.Directions = (direction1: ZoopDirection.x, direction2: ZoopDirection.y);
    return plane;
  }

  private static void CalculateZoopPlane(Vector3 startPos, Vector3 endPos, ZoopPlane plane)
  {
    var ax = (dir: ZoopDirection.x, abs: Math.Abs(endPos.x - startPos.x) / 2, s: startPos.x, e: endPos.x);
    var ay = (dir: ZoopDirection.y, abs: Math.Abs(endPos.y - startPos.y) / 2, s: startPos.y, e: endPos.y);
    var az = (dir: ZoopDirection.z, abs: Math.Abs(endPos.z - startPos.z) / 2, s: startPos.z, e: endPos.z);

    if (ay.abs > ax.abs) (ax, ay) = (ay, ax);
    if (az.abs > ax.abs) (ax, az) = (az, ax);
    if (az.abs > ay.abs) (ay, _) = (az, ay);

    var (cnt1, inc1) = AxisMetrics(ax.s, ax.e);
    var (cnt2, inc2) = AxisMetrics(ay.s, ay.e);

    plane.Directions = (direction1: ax.dir, direction2: ay.dir);
    plane.Count = (direction1: cnt1, direction2: cnt2);
    plane.Increasing = (direction1: inc1, direction2: inc2);
  }

  private static (int cnt, bool inc) AxisMetrics(float s, float e)
    => (1 + (int)(Math.Abs(s - e) / 2), s < e);
}
