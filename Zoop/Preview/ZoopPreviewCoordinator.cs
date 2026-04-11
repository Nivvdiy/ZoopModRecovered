using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Preview.Strategies;

namespace ZoopMod.Zoop.Preview;

/// <summary>
/// Owns the live zoop preview loop, including cursor-movement gating and preview reconstruction.
/// </summary>
internal sealed class ZoopPreviewCoordinator(ZoopPreviewValidator previewValidator)
{
  private readonly IZoopPreviewGridStrategy[] gridStrategies =
  [
    new ZoopSmallGridPreviewStrategy(previewValidator),
    new ZoopBigGridPreviewStrategy(previewValidator),
  ];

  private ZoopDraft activeDraft;
  private ZoopPreviewCache activePreviewCache;
  private Coroutine previewLoopCoroutine;
  private InventoryManager previewLoopOwner;
  private Vector3? lastPreviewCursorPosition;
  private bool previewDirty = true;
  private readonly List<Structure> fullFidelityPieces = new();

  // Amount of pieces that will be rendered fully during Build Preview
  private const int FullFidelityCount = 1;

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
    StopLoop();
    StripVisualMonoBehaviours();
  }

  /// <summary>
  /// Stops the preview loop and restores full-fidelity visuals on ALL preview pieces.
  /// Used when entering the pending-build state so pieces look correct during the build wait.
  /// The preview loop is no longer running, so the enabled MBs have no per-frame cost.
  /// </summary>
  public void StopForPendingBuild(ZoopDraft draft)
  {
    StopLoop();
    fullFidelityPieces.Clear();
    // Re-enable visual MonoBehaviours on only the last N pieces (same as during preview)
    // so the cursor-end looks correct without re-introducing per-frame cost on all pieces.
    if (draft != null)
      EnableVisualMonoBehavioursOnLastN(draft.PreviewPieces);
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
      UpdatePreviewStep(draft, previewCache, inventoryManager, currentPos.Value).GetAwaiter()
        .GetResult();
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

  internal IZoopPreviewGridStrategy FindStrategy(Structure cursor)
    => cursor != null ? Array.Find(gridStrategies, s => s.Matches(cursor)) : null;

  internal Vector3? GetCurrentMouseGridPosition()
  {
    var cursor = InventoryManager.ConstructionCursor;
    if (cursor == null)
    {
      return null;
    }

    return FindStrategy(cursor)?.GetCursorPosition(cursor);
  }

  private IEnumerator PreviewLoop(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager)
  {
    InventoryManager.ConstructionCursor?.gameObject.SetActive(false);

    while (ReferenceEquals(activeDraft, draft) &&
           ReferenceEquals(activePreviewCache, previewCache))
    {
      var currentPos = GetCurrentMouseGridPosition();
      var shouldRefresh = currentPos.HasValue && ShouldRefreshPreview(currentPos.Value);
      if (shouldRefresh)
      {
        Exception previewException = null;
        yield return UpdatePreviewStep(draft, previewCache, inventoryManager, currentPos.Value)
          .ToCoroutine(exception => { previewException = exception; });

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

  private async UniTask UpdatePreviewStep(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager, Vector3 currentPos)
  {
    if (draft.Waypoints.Count <= 0)
    {
      return;
    }

    draft.HasError = false;

    var cursor = InventoryManager.ConstructionCursor;
    var strategy = Array.Find(gridStrategies, s => s.Matches(cursor));
    if (strategy != null)
    {
      await strategy.UpdatePreview(draft, previewCache, inventoryManager, currentPos);
    }

    UpdateFullFidelityPieces(draft);

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
           !ZoopPositionUtility.IsSameZoopPosition(lastPreviewCursorPosition.Value, currentPos);
  }

  private void UpdateFullFidelityPieces(ZoopDraft draft)
  {
    // Disable visual MBs on pieces that were previously in the full-fidelity set.
    StripVisualMonoBehaviours();
    // Re-enable visual MBs on the last N pieces so they render with correct blueprint visuals.
    EnableVisualMonoBehavioursOnLastN(draft.PreviewPieces);
  }

  private void StopLoop()
  {
    if (previewLoopOwner != null && previewLoopCoroutine != null)
      previewLoopOwner.StopCoroutine(previewLoopCoroutine);
    previewLoopCoroutine = null;
    previewLoopOwner = null;
    activeDraft = null;
    activePreviewCache = null;
    lastPreviewCursorPosition = null;
    previewDirty = false;
  }

  private void EnableVisualMonoBehavioursOnLastN(IList<PreviewPiece> pieces)
  {
    var startIndex = Math.Max(0, pieces.Count - FullFidelityCount);
    for (var i = startIndex; i < pieces.Count; i++)
    {
      var structure = pieces[i].Structure;
      if (structure == null) continue;
      foreach (var mb in structure.GetComponentsInChildren<MonoBehaviour>(true))
      {
        if (mb is not Thing) mb.enabled = true;
      }
      fullFidelityPieces.Add(structure);
    }
  }

  private void StripVisualMonoBehaviours()
  {
    foreach (var structure in fullFidelityPieces)
    {
      if (structure == null) continue;
      foreach (var mb in structure.GetComponentsInChildren<MonoBehaviour>(true))
      {
        if (mb is not Thing) mb.enabled = false;
      }
    }
    fullFidelityPieces.Clear();
  }
}
