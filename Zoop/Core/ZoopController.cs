using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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
/// Owns the live zoop session lifecycle, including start/cancel handling, waypoint management,
/// preview refresh, and final build execution.
/// </summary>
internal sealed class ZoopController(
  ZoopPreviewValidator previewValidator,
  ZoopPlacementUpdateGate placementUpdateGate)
{
  private enum ZoopLifecycleState
  {
    Idle,
    Previewing,
    PendingBuild
  }

  private const int DefaultSpacing = 1;
  private static readonly MethodInfo WaitUntilDoneMethod = typeof(InventoryManager).GetMethod("WaitUntilDone",
    BindingFlags.NonPublic | BindingFlags.Instance, null,
    [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
    null);

  private readonly ZoopSmallGridCoordinator smallGridCoordinator = new(previewValidator);
  private readonly ZoopBigGridCoordinator bigGridCoordinator = new(previewValidator);
  private ZoopDraft activeDraft;
  private ZoopPreviewCache activePreviewCache;
  private CancellationTokenSource previewCancellationSource;
  private ZoopLifecycleState state;
  private Coroutine pendingBuildCoroutine;
  private InventoryManager pendingBuildOwner;
  private ZoopBuildPlan pendingBuildPlan;
  private int currentPreviewLoopId;
  private int nextPreviewLoopId;

  public int PreviewCount => activeDraft?.PreviewCount ?? 0;
  public bool AllowPlacementUpdate => placementUpdateGate.AllowPlacementUpdate;
  public bool IsZoopKeyPressed { get; set; }
  public bool IsZooping => state != ZoopLifecycleState.Idle;
  public bool IsPreviewing => state == ZoopLifecycleState.Previewing;
  public Color LineColor { get; set; } = Color.green;

  public void ToggleZoop(InventoryManager inventoryManager)
  {
    if (IsZooping)
    {
      CancelZoop();
      return;
    }

    BeginZoop(inventoryManager);
  }

  public void RestartZoop(InventoryManager inventoryManager)
  {
    if (!IsZooping)
    {
      return;
    }

    BeginZoop(inventoryManager, restartExisting: true);
  }

  public void CancelZoop()
  {
    ResetSession(restoreCursorVisibility: true, cancelPendingBuild: true);
  }

  public void ConfirmZoop(InventoryManager inventoryManager)
  {
    var draft = activeDraft;
    if (!IsPreviewing || draft == null || draft.HasError)
    {
      return;
    }

    var buildPlan = CaptureBuildPlan(draft, inventoryManager);
    if (buildPlan.Count <= 0)
    {
      CancelZoop();
      return;
    }

    EnterPendingBuild(buildPlan);

    if (!InventoryManager.IsAuthoringMode &&
        InventoryManager.ConstructionCursor != null &&
        InventoryManager.ConstructionCursor.BuildPlacementTime > 0.0)
    {
      if (TrySchedulePendingBuild(inventoryManager))
      {
        return;
      }

      ZoopLog.Error("[Build] Unable to start delayed zoop build; building immediately.");
    }

    BuildPendingZoop(inventoryManager);
  }

  public void AddWaypoint()
  {
    var draft = activeDraft;
    if (!IsPreviewing || draft == null || !ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    var currentPos = GetCurrentMouseGridPosition();
    var lastWaypoint = draft.Waypoints[draft.Waypoints.Count - 1];
    if (currentPos.HasValue && !IsSameZoopPosition(lastWaypoint, currentPos.Value))
    {
      if (draft.PreviewCount > 0 &&
          IsSameZoopPosition(draft.PreviewPieces[draft.PreviewCount - 1].Structure.Position, currentPos.Value))
      {
        draft.Waypoints.Add(currentPos.Value);
      }
    }
    else if (currentPos.HasValue && IsSameZoopPosition(lastWaypoint, currentPos.Value))
    {
      // TODO show message to user that waypoint is already added
    }
  }

  public void RemoveLastWaypoint()
  {
    var draft = activeDraft;
    if (!IsPreviewing || draft == null || !ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    if (draft.Waypoints.Count > 1)
    {
      draft.Waypoints.RemoveAt(draft.Waypoints.Count - 1);
    }
  }

  private void BeginZoop(InventoryManager inventoryManager, bool restartExisting = false)
  {
    if (!ZoopConstructableRules.IsAllowed(InventoryManager.ConstructionCursor))
    {
      if (restartExisting)
      {
        CancelZoop();
      }

      return;
    }

    if (restartExisting || IsZooping)
    {
      ResetSession(restoreCursorVisibility: false, cancelPendingBuild: true);
    }

    var draft = new ZoopDraft
    {
      ZoopSpawnPrefab = InventoryManager.SpawnPrefab
    };
    activeDraft = draft;
    activePreviewCache = new ZoopPreviewCache();

    if (InventoryManager.ConstructionCursor != null)
    {
      var selectedConstructable = GetSelectedConstructable(inventoryManager);
      using (placementUpdateGate.BeginScope())
      {
        if (selectedConstructable != null)
        {
          InventoryManager.UpdatePlacement(selectedConstructable);
        }
      }

      draft.ZoopStartRotation = InventoryManager.ConstructionCursor.transform.rotation;
      draft.ZoopStartWallNormal = InventoryManager.ConstructionCursor is Wall
        ? GetCardinalAxis(InventoryManager.ConstructionCursor.transform.forward)
        : Vector3.zero;

      var startPos = GetCurrentMouseGridPosition();
      if (startPos.HasValue)
      {
        draft.Waypoints.Add(startPos.Value);
      }
    }

    if (draft.Waypoints.Count <= 0)
    {
      ResetSession(restoreCursorVisibility: true, cancelPendingBuild: true);
      return;
    }

    state = ZoopLifecycleState.Previewing;
    var cts = new CancellationTokenSource();
    previewCancellationSource = cts;
    var previewLoopId = Interlocked.Increment(ref nextPreviewLoopId);
    currentPreviewLoopId = previewLoopId;
    var ct = cts.Token;
    UniTask.RunOnThreadPool(async () =>
    {
      try
      {
        await ZoopAsync(ct, inventoryManager);
      }
      finally
      {
        cts.Dispose();
        if (previewLoopId == currentPreviewLoopId && ReferenceEquals(previewCancellationSource, cts))
        {
          previewCancellationSource = null;
        }
      }
    }, cancellationToken: ct);
  }

  private void BuildPendingZoop(InventoryManager inventoryManager)
  {
    var buildPlan = ConsumePendingBuildPlan();
    if (buildPlan == null)
    {
      return;
    }

    ResetSession(restoreCursorVisibility: false, cancelPendingBuild: false);
    ZoopBuildExecutor.BuildAll(inventoryManager, buildPlan);
    inventoryManager.CancelPlacement();
  }

  private static ZoopBuildPlan CaptureBuildPlan(ZoopDraft draft, InventoryManager inventoryManager)
  {
    var pieces = new List<ZoopBuildPiece>(draft.PreviewCount);
    for (var structureIndex = 0; structureIndex < draft.PreviewCount; structureIndex++)
    {
      var previewPiece = draft.PreviewPieces[structureIndex];
      var previewStructure = previewPiece.Structure;
      var buildIndex = ZoopConstructableResolver.ResolveBuildIndex(draft, inventoryManager, previewStructure, structureIndex);
      if (buildIndex < 0)
      {
        ZoopLog.Error($"[Build] Unable to resolve build index for {previewStructure.PrefabName}; skipping zoop placement.");
        continue;
      }

      var spawnPrefab = ResolveSpawnPrefabForBuild(draft, inventoryManager, buildIndex, previewStructure);
      if (spawnPrefab == null)
      {
        ZoopLog.Error($"[Build] Unable to resolve spawn prefab for build index {buildIndex}; skipping zoop placement.");
        continue;
      }

      pieces.Add(new ZoopBuildPiece(
        spawnPrefab,
        buildIndex,
        previewStructure.transform.position,
        previewStructure.transform.rotation));
    }

    return new ZoopBuildPlan(pieces);
  }

  private async UniTask ZoopAsync(CancellationToken cancellationToken, InventoryManager inventoryManager)
  {
    var draft = activeDraft;
    var previewCache = activePreviewCache;
    if (draft == null || previewCache == null)
    {
      return;
    }

    await UniTask.SwitchToMainThread();

    List<ZoopSegment> segments = [];
    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(false);
    }

    while (cancellationToken is { IsCancellationRequested: false })
    {
      try
      {
        if (draft.Waypoints.Count > 0)
        {
          var currentPos = GetCurrentMouseGridPosition();
          if (currentPos.HasValue)
          {
            draft.HasError = false;

            if (IsZoopingSmallGrid())
            {
              await smallGridCoordinator.UpdatePreview(draft, previewCache, inventoryManager, currentPos.Value, segments,
                DefaultSpacing);
            }
            else if (IsZoopingBigGrid())
            {
              await bigGridCoordinator.UpdatePreview(draft, previewCache, inventoryManager, currentPos.Value,
                DefaultSpacing);
            }

            foreach (var previewPiece in draft.PreviewPieces)
            {
              ZoopPreviewColorizer.ApplyColor(inventoryManager, previewPiece.Structure, draft.Waypoints, draft.HasError,
                LineColor);
            }
          }
        }

        await UniTask.Delay(100, DelayType.Realtime, cancellationToken: cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception e)
      {
        ZoopLog.Error(e, "Preview update loop failed.");
      }
    }
  }

  private void CancelPendingBuild()
  {
    if (pendingBuildOwner != null && pendingBuildCoroutine != null)
    {
      pendingBuildOwner.StopCoroutine(pendingBuildCoroutine);
    }

    ClearPendingBuildState();
  }

  private void ClearPendingBuildState()
  {
    pendingBuildCoroutine = null;
    pendingBuildOwner = null;
    pendingBuildPlan = null;
  }

  private ZoopBuildPlan ConsumePendingBuildPlan()
  {
    var buildPlan = pendingBuildPlan;
    ClearPendingBuildState();
    return buildPlan;
  }

  private void EnterPendingBuild(ZoopBuildPlan buildPlan)
  {
    StopPreviewLoop(ZoopLifecycleState.PendingBuild);
    pendingBuildPlan = buildPlan;
  }

  private bool TrySchedulePendingBuild(InventoryManager inventoryManager)
  {
    if (WaitUntilDoneMethod == null || InventoryManager.ConstructionCursor == null)
    {
      return false;
    }

    try
    {
      var actionCoroutine = inventoryManager.StartCoroutine((IEnumerator)WaitUntilDoneMethod.Invoke(
        inventoryManager,
        [
          new InventoryManager.DelegateEvent(() => BuildPendingZoop(inventoryManager)),
          InventoryManager.ConstructionCursor.BuildPlacementTime,
          InventoryManager.ConstructionCursor
        ]));

      if (actionCoroutine == null)
      {
        return false;
      }

      pendingBuildCoroutine = actionCoroutine;
      pendingBuildOwner = inventoryManager;
      return true;
    }
    catch (Exception exception)
    {
      ZoopLog.Error(exception, "[Build] Failed to schedule delayed zoop build.");
      return false;
    }
  }

  private static Assets.Scripts.ICreativeSpawnable ResolveSpawnPrefabForBuild(ZoopDraft draft, InventoryManager inventoryManager,
    int buildIndex, Structure previewStructure)
  {
    if (InventoryManager.IsAuthoringMode && draft.ZoopSpawnPrefab != null)
    {
      return draft.ZoopSpawnPrefab;
    }

    return ZoopConstructableResolver.GetConstructableForBuildIndex(inventoryManager, buildIndex) ?? previewStructure;
  }

  private void StopPreviewLoop(ZoopLifecycleState nextState)
  {
    currentPreviewLoopId = 0;
    var cancellationSource = previewCancellationSource;
    previewCancellationSource = null;
    state = nextState;
    cancellationSource?.Cancel();
  }

  private void ResetSession(bool restoreCursorVisibility, bool cancelPendingBuild)
  {
    if (cancelPendingBuild)
    {
      CancelPendingBuild();
    }
    else
    {
      ClearPendingBuildState();
    }

    StopPreviewLoop(ZoopLifecycleState.Idle);
    if (activePreviewCache != null)
    {
      ZoopPreviewFactory.ClearStructureCache(activePreviewCache);
    }

    activeDraft = null;
    activePreviewCache = null;

    if (restoreCursorVisibility)
    {
      RestoreConstructionCursorVisibility();
    }
  }

  private static void RestoreConstructionCursorVisibility()
  {
    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(true);
    }
  }

  private static bool IsSameZoopPosition(Vector3 first, Vector3 second)
  {
    return Vector3.SqrMagnitude(first - second) < ZoopPreviewColorizer.PositionToleranceSqr;
  }

  private static Structure GetSelectedConstructable(InventoryManager inventoryManager)
  {
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    var selectedIndex = inventoryManager.ConstructionPanel.Parent.LastSelectedIndex;
    if (selectedIndex >= 0 && selectedIndex < constructables.Count)
    {
      return constructables[selectedIndex];
    }

    return InventoryManager.ConstructionCursor;
  }

  private static bool IsZoopingSmallGrid()
  {
    return InventoryManager.ConstructionCursor is SmallGrid;
  }

  private static bool IsZoopingBigGrid()
  {
    return InventoryManager.ConstructionCursor is LargeStructure;
  }

  private static Vector3 GetCardinalAxis(Vector3 vector)
  {
    var normalized = vector.normalized;
    var xAbs = Mathf.Abs(normalized.x);
    var yAbs = Mathf.Abs(normalized.y);
    var zAbs = Mathf.Abs(normalized.z);

    if (xAbs >= yAbs && xAbs >= zAbs)
    {
      return normalized.x >= 0f ? Vector3.right : Vector3.left;
    }

    if (yAbs >= zAbs)
    {
      return normalized.y >= 0f ? Vector3.up : Vector3.down;
    }

    return normalized.z >= 0f ? Vector3.forward : Vector3.back;
  }

  private static Vector3? GetCurrentMouseGridPosition()
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
}
