using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
  private const float PendingBuildStartDelaySeconds = 0.050f;
  private static readonly MethodInfo WaitUntilDoneMethod = typeof(InventoryManager).GetMethod("WaitUntilDone",
    BindingFlags.NonPublic | BindingFlags.Instance, null,
    [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
    null);

  private readonly ZoopSmallGridCoordinator smallGridCoordinator = new(previewValidator);
  private readonly ZoopBigGridCoordinator bigGridCoordinator = new(previewValidator);
  private ZoopDraft activeDraft;
  private ZoopPreviewCache activePreviewCache;
  private Coroutine previewLoopCoroutine;
  private InventoryManager previewLoopOwner;
  private ZoopLifecycleState state;
  private Vector3? lastPreviewCursorPosition;
  private bool previewDirty = true;
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
  public Color LineColor { get; set; } = Color.green;

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
    var draft = activeDraft;
    if (!IsPreviewing || draft == null || draft.HasError)
    {
      ZoopLog.Debug($"[Build] Confirm ignored. IsPreviewing={IsPreviewing}, DraftPresent={draft != null}, HasError={draft?.HasError ?? false}.");
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

    var currentPos = GetCurrentMouseGridPosition();
    var lastWaypoint = draft.Waypoints[draft.Waypoints.Count - 1];
    ZoopLog.Debug($"[Waypoint] Add requested. CurrentPos={(currentPos.HasValue ? currentPos.Value.ToString() : "<none>")}, LastWaypoint={lastWaypoint}, PreviewCount={draft.PreviewCount}, WaypointCount={draft.Waypoints.Count}.");
    if (currentPos.HasValue && !IsSameZoopPosition(lastWaypoint, currentPos.Value))
    {
      draft.Waypoints.Add(currentPos.Value);
      InvalidatePreview();
      ZoopLog.Debug($"[Waypoint] Added waypoint at {currentPos.Value} directly from the snapped cursor position. NewWaypointCount={draft.Waypoints.Count}.");
    }
    else if (currentPos.HasValue && IsSameZoopPosition(lastWaypoint, currentPos.Value))
    {
      ZoopLog.Debug("[Waypoint] Add ignored because the current cursor position already matches the last waypoint.");
      // TODO show message to user that waypoint is already added
    }
    else
    {
      ZoopLog.Debug("[Waypoint] Add ignored because no current grid position was available.");
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
      InvalidatePreview();
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

      var startPos = GetCurrentMouseGridPosition();
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
    InvalidatePreview();
    ZoopLog.Debug($"[Lifecycle] Zoop preview started with {draft.Waypoints.Count} waypoint(s).");
    StartPreviewLoop(inventoryManager);
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

  private async UniTask UpdatePreviewStep(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager,
    List<ZoopSegment> segments)
  {
    if (draft.Waypoints.Count <= 0)
    {
      return;
    }

    var currentPos = GetCurrentMouseGridPosition();
    if (!currentPos.HasValue)
    {
      return;
    }

    draft.HasError = false;

    if (IsZoopingSmallGrid())
    {
      await smallGridCoordinator.UpdatePreview(draft, previewCache, inventoryManager, currentPos.Value, segments,
        DefaultSpacing);
    }
    else if (IsZoopingBigGrid())
    {
      await bigGridCoordinator.UpdatePreview(draft, previewCache, inventoryManager, currentPos.Value, DefaultSpacing);
    }

    foreach (var previewPiece in draft.PreviewPieces)
    {
      ZoopPreviewColorizer.ApplyColor(inventoryManager, previewPiece.Structure, draft.Waypoints, draft.HasError,
        LineColor);
    }
  }

  private void CancelPendingBuild()
  {
    if (pendingBuildOwner != null && pendingBuildCoroutine != null)
    {
      ZoopLog.Debug("[Build] Stopping pending delayed build coroutine.");
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
      var actionCoroutine = inventoryManager.StartCoroutine(BeginPendingBuildAfterDelay(inventoryManager));

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

  /// <summary>
  /// Adds a short realtime pause before entering the game's native build wait coroutine.
  /// This gives the construction cursor time to settle after fast tool/build-option changes,
  /// which avoids native wait cancellation caused by transitional cursor state.
  /// </summary>
  private IEnumerator BeginPendingBuildAfterDelay(InventoryManager inventoryManager)
  {
    ZoopLog.Debug($"[Build] Waiting {PendingBuildStartDelaySeconds:0.###} seconds before starting native delayed build.");
    yield return new WaitForSecondsRealtime(PendingBuildStartDelaySeconds);

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
    InvalidatePreview();
    ZoopLog.Debug($"[Lifecycle] Resumed zoop preview with {activeDraft.Waypoints.Count} waypoint(s).");
    StartPreviewLoop(inventoryManager);
  }

  private void StartPreviewLoop(InventoryManager inventoryManager)
  {
    var draft = activeDraft;
    var previewCache = activePreviewCache;
    if (draft == null || previewCache == null)
    {
      return;
    }

    previewLoopOwner = inventoryManager;
    previewLoopCoroutine = inventoryManager.StartCoroutine(PreviewLoop(draft, previewCache, inventoryManager));
  }

  private IEnumerator PreviewLoop(ZoopDraft draft, ZoopPreviewCache previewCache, InventoryManager inventoryManager)
  {
    var segments = new List<ZoopSegment>();
    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(false);
    }

    while (state == ZoopLifecycleState.Previewing &&
           ReferenceEquals(activeDraft, draft) &&
           ReferenceEquals(activePreviewCache, previewCache))
    {
      var currentPos = GetCurrentMouseGridPosition();
      var shouldRefresh = currentPos.HasValue && ShouldRefreshPreview(currentPos.Value);
      if (shouldRefresh)
      {
        Exception previewException = null;
        yield return UpdatePreviewStep(draft, previewCache, inventoryManager, segments).ToCoroutine(exception =>
        {
          previewException = exception;
        });

        if (previewException != null)
        {
          ZoopLog.Error(previewException, "Preview update loop failed.");
          InvalidatePreview();
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

  private void StopPreviewLoop(ZoopLifecycleState nextState)
  {
    if (previewLoopOwner != null && previewLoopCoroutine != null)
    {
      previewLoopOwner.StopCoroutine(previewLoopCoroutine);
    }

    previewLoopCoroutine = null;
    previewLoopOwner = null;
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
    lastPreviewCursorPosition = null;
    previewDirty = false;

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

  private void InvalidatePreview()
  {
    previewDirty = true;
    lastPreviewCursorPosition = null;
  }

  private bool ShouldRefreshPreview(Vector3 currentPos)
  {
    return previewDirty ||
           !lastPreviewCursorPosition.HasValue ||
           !IsSameZoopPosition(lastPreviewCursorPosition.Value, currentPos);
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
