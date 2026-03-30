using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Placement;
using ZoopMod.Zoop.Planning;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Core;

/// <summary>
/// Owns the live zoop preview loop, including cursor-movement gating and preview reconstruction.
/// </summary>
internal sealed class ZoopPreviewCoordinator(ZoopPreviewValidator previewValidator)
{
  private readonly ZoopSmallGridCoordinator smallGridCoordinator = new(previewValidator);
  private readonly ZoopBigGridCoordinator bigGridCoordinator = new(previewValidator);

  private ZoopDraft activeDraft;
  private ZoopPreviewCache activePreviewCache;
  private Coroutine previewLoopCoroutine;
  private InventoryManager previewLoopOwner;
  private Vector3? lastPreviewCursorPosition;
  private bool previewDirty = true;

  public Color LineColor { get; set; } = Color.green;

  public void Start(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager)
  {
    if (draft == null || previewCache == null || inventoryManager == null)
    {
      return;
    }

    Stop();
    activeDraft = draft;
    activePreviewCache = previewCache;
    Invalidate();
    previewLoopOwner = inventoryManager;
    previewLoopCoroutine = inventoryManager.StartCoroutine(PreviewLoop(draft, previewCache, inventoryManager));
  }

  public void Stop()
  {
    if (previewLoopOwner != null && previewLoopCoroutine != null)
    {
      previewLoopOwner.StopCoroutine(previewLoopCoroutine);
    }

    previewLoopCoroutine = null;
    previewLoopOwner = null;
    activeDraft = null;
    activePreviewCache = null;
    lastPreviewCursorPosition = null;
    previewDirty = false;
  }

  public void Invalidate()
  {
    previewDirty = true;
    lastPreviewCursorPosition = null;
  }

  public bool TryFinalizePreview(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager)
  {
    if (draft == null || previewCache == null || inventoryManager == null)
    {
      return false;
    }

    var currentPos = GetCurrentMouseGridPosition();
    if (!currentPos.HasValue)
    {
      return false;
    }

    try
    {
      UpdatePreviewStep(draft, previewCache, inventoryManager, new List<ZoopSegment>(), currentPos.Value).GetAwaiter().GetResult();
      lastPreviewCursorPosition = currentPos.Value;
      previewDirty = false;
      return true;
    }
    catch (Exception exception)
    {
      ZoopLog.Error(exception, "[Build] Failed to finalize zoop preview.");
      Invalidate();
      return false;
    }
  }

  internal static Vector3? GetCurrentMouseGridPosition()
  {
    if (InventoryManager.ConstructionCursor == null)
    {
      return null;
    }

    if (InventoryManager.ConstructionCursor is Wall)
    {
      return InventoryManager.ConstructionCursor.ThingTransformPosition;
    }

    return InventoryManager.ConstructionCursor.GetLocalGrid().ToVector3();
  }

  private IEnumerator PreviewLoop(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager)
  {
    var segments = new List<ZoopSegment>();
    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(false);
    }

    while (ReferenceEquals(activeDraft, draft) &&
           ReferenceEquals(activePreviewCache, previewCache))
    {
      var currentPos = GetCurrentMouseGridPosition();
      var shouldRefresh = currentPos.HasValue && ShouldRefreshPreview(currentPos.Value);
      if (shouldRefresh)
      {
        Exception previewException = null;
        yield return UpdatePreviewStep(draft, previewCache, inventoryManager, segments, currentPos.Value).ToCoroutine(exception =>
        {
          previewException = exception;
        });

        if (previewException != null)
        {
          ZoopLog.Error(previewException, "Preview update loop failed.");
          Invalidate();
        }
        else
        {
          lastPreviewCursorPosition = currentPos.Value;
          previewDirty = false;
        }
      }

      yield return null;
    }
  }

  private async UniTask UpdatePreviewStep(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    List<ZoopSegment> segments, Vector3 currentPos)
  {
    if (draft.Waypoints.Count <= 0)
    {
      return;
    }

    draft.HasError = false;

    if (IsZoopingSmallGrid())
    {
      await smallGridCoordinator.UpdatePreview(draft, previewCache, inventoryManager, currentPos, segments, 1);
    }
    else if (IsZoopingBigGrid())
    {
      await bigGridCoordinator.UpdatePreview(draft, previewCache, inventoryManager, currentPos, 1);
    }

    foreach (var previewPiece in draft.PreviewPieces)
    {
      ZoopPreviewColorizer.ApplyColor(inventoryManager, previewPiece.Structure, draft.Waypoints, draft.HasError,
        LineColor);
    }
  }

  private bool ShouldRefreshPreview(Vector3 currentPos)
  {
    return previewDirty ||
           !lastPreviewCursorPosition.HasValue ||
           !IsSameZoopPosition(lastPreviewCursorPosition.Value, currentPos);
  }

  private static bool IsSameZoopPosition(Vector3 first, Vector3 second)
  {
    return Vector3.SqrMagnitude(first - second) < ZoopPreviewColorizer.PositionToleranceSqr;
  }

  private static bool IsZoopingSmallGrid()
  {
    return InventoryManager.ConstructionCursor is SmallGrid;
  }

  private static bool IsZoopingBigGrid()
  {
    return InventoryManager.ConstructionCursor is LargeStructure;
  }
}
