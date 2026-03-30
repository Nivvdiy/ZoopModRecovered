using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Placement;
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

  private enum WaypointCaptureResult
  {
    Added,
    Duplicate,
    MissingCursor
  }

  private const float ConfirmDraftDelaySeconds = 0.100f;
  private static readonly MethodInfo WaitUntilDoneMethod = typeof(InventoryManager).GetMethod("WaitUntilDone",
    BindingFlags.NonPublic | BindingFlags.Instance, null,
    [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
    null);

  private readonly ZoopPreviewCoordinator previewCoordinator = new(previewValidator);
  private ZoopDraft activeDraft;
  private ZoopPreviewCache activePreviewCache;
  private ZoopLifecycleState state;
  private Coroutine pendingBuildCoroutine;
  private InventoryManager pendingBuildOwner;
  private ZoopBuildPlan pendingBuildPlan;
  private Coroutine buildExecutionCoroutine;
  private InventoryManager buildExecutionOwner;

  public int PreviewCount => activeDraft?.PreviewCount ?? 0;
  public bool AllowPlacementUpdate => placementUpdateGate.AllowPlacementUpdate;
  public bool IsBuildExecuting => buildExecutionCoroutine != null;
  public bool IsZoopKeyPressed { get; set; }
  public bool IsZooping => state != ZoopLifecycleState.Idle;
  public bool IsPreviewing => state == ZoopLifecycleState.Previewing;

  public Color LineColor
  {
    get => previewCoordinator.LineColor;
    set => previewCoordinator.LineColor = value;
  }

  public void ToggleZoop(InventoryManager inventoryManager)
  {
    if (IsBuildExecuting)
    {
      ZoopLog.Debug("[Lifecycle] Ignored zoop toggle while an automatic zoop build is still executing.");
      return;
    }

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
    if (IsBuildExecuting)
    {
      ZoopLog.Debug("[Lifecycle] Canceling active zoop build execution.");
      CancelBuildExecution();
      return;
    }

    ZoopLog.Debug($"[Lifecycle] Canceling zoop from state {state}.");
    ResetSession(restoreCursorVisibility: true, cancelPendingBuild: true);
  }

  public void ConfirmZoop(InventoryManager inventoryManager)
  {
    try
    {
      var confirmCoroutine = inventoryManager.StartCoroutine(ConfirmZoopAfterDelay(inventoryManager));
      if (confirmCoroutine != null)
      {
        pendingBuildCoroutine = confirmCoroutine;
        pendingBuildOwner = inventoryManager;
        ZoopLog.Debug($"[Build] Waiting {ConfirmDraftDelaySeconds:0.###} seconds before confirming zoop draft.");
        return;
      }
    }
    catch (Exception exception)
    {
      ZoopLog.Error(exception, "[Build] Failed to schedule delayed zoop confirm.");
      return;
    }

    ZoopLog.Error("[Build] Unable to start delayed zoop confirm.");
  }

  private IEnumerator ConfirmZoopAfterDelay(InventoryManager inventoryManager)
  {
    yield return new WaitForSecondsRealtime(ConfirmDraftDelaySeconds);

    ClearPendingBuildState();
    FinalizeConfirmedZoop(inventoryManager);
  }

  private void FinalizeConfirmedZoop(InventoryManager inventoryManager)
  {
    var draft = activeDraft;
    if (!IsPreviewing || draft == null)
    {
      ZoopLog.Debug($"[Build] Confirm ignored. IsPreviewing={IsPreviewing}, DraftPresent={draft != null}.");
      return;
    }

    if (!previewCoordinator.TryFinalizePreview(draft, activePreviewCache, inventoryManager))
    {
      ZoopLog.Debug("[Build] Confirm could not finalize the latest preview state; using the current draft preview.");
    }

    if (draft.HasError)
    {
      ZoopLog.Debug("[Build] Confirm ignored because the finalized zoop preview has errors.");
      return;
    }

    var buildPlan = CaptureBuildPlan(draft, inventoryManager);
    if (buildPlan.Count <= 0)
    {
      ZoopLog.Debug("[Build] Confirm produced an empty build plan; canceling zoop.");
      CancelZoop();
      return;
    }

    ZoopLog.Debug($"[Build] Confirmed zoop with {buildPlan.Count} planned piece(s).");
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
    var supportsWaypoints = ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor);
    if (!IsPreviewing || draft == null || !supportsWaypoints)
    {
      ZoopLog.Debug($"[Waypoint] Add ignored. IsPreviewing={IsPreviewing}, DraftPresent={draft != null}, SupportsWaypoints={supportsWaypoints}.");
      return;
    }

    var currentPos = ZoopPreviewCoordinator.GetCurrentMouseGridPosition();
    var lastWaypoint = draft.Waypoints[draft.Waypoints.Count - 1];
    ZoopLog.Debug($"[Waypoint] Add requested. CurrentPos={(currentPos.HasValue ? currentPos.Value.ToString() : "<none>")}, LastWaypoint={lastWaypoint}, PreviewCount={draft.PreviewCount}, WaypointCount={draft.Waypoints.Count}.");
    switch (TryCaptureCurrentWaypoint(draft, invalidatePreview: true, out var capturedPos))
    {
      case WaypointCaptureResult.Added:
        ZoopLog.Debug($"[Waypoint] Added waypoint at {capturedPos} directly from the snapped cursor position. NewWaypointCount={draft.Waypoints.Count}.");
        break;
      case WaypointCaptureResult.Duplicate:
        ZoopLog.Debug("[Waypoint] Add ignored because the current cursor position already matches the last waypoint.");
        // TODO show message to user that waypoint is already added
        break;
      case WaypointCaptureResult.MissingCursor:
        ZoopLog.Debug("[Waypoint] Add ignored because no current grid position was available.");
        break;
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
      previewCoordinator.Invalidate();
    }
  }

  private void BeginZoop(InventoryManager inventoryManager, bool restartExisting = false)
  {
    if (!ZoopConstructableRules.IsAllowed(InventoryManager.ConstructionCursor))
    {
      ZoopLog.Debug("[Lifecycle] Ignored zoop start because the current constructable is not supported.");
      if (restartExisting)
      {
        CancelZoop();
      }

      return;
    }

    if (restartExisting || IsZooping)
    {
      ZoopLog.Debug($"[Lifecycle] Resetting existing zoop before {(restartExisting ? "restart" : "start")}.");
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

      var startPos = ZoopPreviewCoordinator.GetCurrentMouseGridPosition();
      if (startPos.HasValue)
      {
        draft.Waypoints.Add(startPos.Value);
      }
    }

    if (draft.Waypoints.Count <= 0)
    {
      ZoopLog.Debug("[Lifecycle] Zoop start failed because no valid starting waypoint was captured.");
      ResetSession(restoreCursorVisibility: true, cancelPendingBuild: true);
      return;
    }

    state = ZoopLifecycleState.Previewing;
    ZoopLog.Debug($"[Lifecycle] Zoop preview started with {draft.Waypoints.Count} waypoint(s).");
    previewCoordinator.Start(draft, activePreviewCache, inventoryManager);
  }

  private void BuildPendingZoop(InventoryManager inventoryManager)
  {
    var buildPlan = ConsumePendingBuildPlan();
    if (buildPlan == null)
    {
      ZoopLog.Debug("[Build] Build callback ignored because no pending build plan remained.");
      return;
    }

    ZoopLog.Debug($"[Build] Executing zoop build with {buildPlan.Count} piece(s).");
    ResetSession(restoreCursorVisibility: false, cancelPendingBuild: false);

    var buildCoroutine = inventoryManager.StartCoroutine(ExecuteBuildPlan(inventoryManager, buildPlan));
    if (buildCoroutine == null)
    {
      ZoopLog.Error("[Build] Failed to start frame-sliced zoop build coroutine; canceling placement cursor.");
      inventoryManager.CancelPlacement();
      return;
    }

    buildExecutionOwner = inventoryManager;
    buildExecutionCoroutine = buildCoroutine;
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

  private void CancelPendingBuild()
  {
    if (pendingBuildOwner != null && pendingBuildCoroutine != null)
    {
      ZoopLog.Debug("[Build] Stopping pending delayed zoop confirm/build coroutine.");
      pendingBuildOwner.StopCoroutine(pendingBuildCoroutine);
    }

    ClearPendingBuildState();
  }

  public void CancelBuildExecution()
  {
    if (buildExecutionOwner != null && buildExecutionCoroutine != null)
    {
      ZoopLog.Debug("[Build] Stopping active zoop build coroutine.");
      buildExecutionOwner.StopCoroutine(buildExecutionCoroutine);
    }

    ClearBuildExecutionState();
  }

  private void ClearPendingBuildState()
  {
    pendingBuildCoroutine = null;
    pendingBuildOwner = null;
    pendingBuildPlan = null;
  }

  private void ClearBuildExecutionState()
  {
    buildExecutionCoroutine = null;
    buildExecutionOwner = null;
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
    ZoopLog.Debug($"[Lifecycle] Entered pending build with {buildPlan.Count} piece(s).");
  }

  private bool TrySchedulePendingBuild(InventoryManager inventoryManager)
  {
    if (WaitUntilDoneMethod == null || InventoryManager.ConstructionCursor == null)
    {
      return false;
    }

    try
    {
      var actionCoroutine = inventoryManager.StartCoroutine(WaitForPendingBuildCompletion(inventoryManager));

      if (actionCoroutine == null)
      {
        return false;
      }

      pendingBuildCoroutine = actionCoroutine;
      pendingBuildOwner = inventoryManager;
      ZoopLog.Debug($"[Build] Scheduled native delayed build for {PreviewCount} preview piece(s).");
      return true;
    }
    catch (Exception exception)
    {
      ZoopLog.Error(exception, "[Build] Failed to schedule delayed zoop build.");
      return false;
    }
  }

  private IEnumerator WaitForPendingBuildCompletion(InventoryManager inventoryManager)
  {
    if (state != ZoopLifecycleState.PendingBuild || pendingBuildPlan == null)
    {
      yield break;
    }
    yield return null;

    if (state != ZoopLifecycleState.PendingBuild || pendingBuildPlan == null)
    {
      yield break;
    }

    if (InventoryManager.ConstructionCursor == null)
    {
      ZoopLog.Debug("[Build] Construction cursor vanished before native delayed build could start; resuming preview.");
      var owner = pendingBuildOwner;
      ClearPendingBuildState();
      ResumePreviewing(owner);
      yield break;
    }

    var onBuildFinished = new InventoryManager.DelegateEvent(() => BuildPendingZoop(inventoryManager));
    var buildTime = InventoryManager.ConstructionCursor.BuildPlacementTime;
    var structure = InventoryManager.ConstructionCursor;
    var waitRoutine = WaitUntilDoneMethod.Invoke(
      inventoryManager,
      [onBuildFinished, buildTime, structure]) as IEnumerator;
    if (waitRoutine == null)
    {
      ZoopLog.Error("[Build] Native delayed build coroutine was unavailable after start delay.");
      var owner = pendingBuildOwner;
      ClearPendingBuildState();
      ResumePreviewing(owner);
      yield break;
    }

    yield return waitRoutine;

    // TODO Restore cursor here?

    if (state != ZoopLifecycleState.PendingBuild || pendingBuildPlan == null)
    {
      yield break;
    }

    ZoopLog.Debug("[Build] Delayed zoop build was interrupted before completion; resuming zoop preview.");
    var inventoryManagerAfterWait = pendingBuildOwner;
    ClearPendingBuildState();
    ResumePreviewing(inventoryManagerAfterWait);
  }

  private IEnumerator ExecuteBuildPlan(InventoryManager inventoryManager, ZoopBuildPlan buildPlan)
  {
    try
    {
      yield return ZoopBuildExecutor.BuildAll(inventoryManager, buildPlan);
    }
    finally
    {
      ClearBuildExecutionState();
    }

    ZoopLog.Debug($"[Build] Zoop build completed for {buildPlan.Count} piece(s); canceling placement cursor.");
    inventoryManager.CancelPlacement();
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

  private void ResumePreviewing(InventoryManager inventoryManager)
  {
    if (inventoryManager == null ||
        activeDraft == null ||
        activePreviewCache == null ||
        activeDraft.Waypoints.Count <= 0 ||
        InventoryManager.ConstructionCursor == null ||
        !ZoopConstructableRules.IsAllowed(InventoryManager.ConstructionCursor))
    {
      ZoopLog.Debug("[Lifecycle] Unable to resume zoop preview after interrupted build; resetting session.");
      ResetSession(restoreCursorVisibility: true, cancelPendingBuild: false);
      return;
    }

    state = ZoopLifecycleState.Previewing;
    ZoopLog.Debug($"[Lifecycle] Resumed zoop preview with {activeDraft.Waypoints.Count} waypoint(s).");
    previewCoordinator.Start(activeDraft, activePreviewCache, inventoryManager);
  }

  private void StopPreviewLoop(ZoopLifecycleState nextState)
  {
    previewCoordinator.Stop();
    state = nextState;
  }

  private void ResetSession(bool restoreCursorVisibility, bool cancelPendingBuild)
  {
    ZoopLog.Debug($"[Lifecycle] Resetting zoop session from state {state}. RestoreCursor={restoreCursorVisibility}, CancelPendingBuild={cancelPendingBuild}.");
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

  private WaypointCaptureResult TryCaptureCurrentWaypoint(ZoopDraft draft, bool invalidatePreview, out Vector3 capturedPos)
  {
    capturedPos = default;

    var currentPos = ZoopPreviewCoordinator.GetCurrentMouseGridPosition();
    if (!currentPos.HasValue)
    {
      return WaypointCaptureResult.MissingCursor;
    }

    capturedPos = currentPos.Value;
    if (ZoopPositionUtility.IsSameZoopPosition(draft.Waypoints[draft.Waypoints.Count - 1], capturedPos))
    {
      return WaypointCaptureResult.Duplicate;
    }

    draft.Waypoints.Add(capturedPos);
    if (invalidatePreview)
    {
      previewCoordinator.Invalidate();
    }

    return WaypointCaptureResult.Added;
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

}
