using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Placement;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Core;

/// <summary>
/// Owns the live zoop session lifecycle, including start/cancel handling, preview refresh,
/// and final build execution. Delegates build scheduling to <see cref="ZoopBuildScheduler"/>
/// and waypoint mutation to <see cref="ZoopWaypointManager"/>.
/// </summary>
internal sealed class ZoopController : IZoopController
{
  private enum ZoopLifecycleState
  {
    Idle,
    Previewing,
    PendingBuild
  }

  private readonly ZoopPlacementUpdateGate placementUpdateGate;
  private readonly ZoopPreviewCoordinator previewCoordinator;
  private readonly ZoopBuildScheduler buildScheduler;
  private readonly ZoopWaypointManager waypointManager;
  private ZoopDraft activeDraft;
  private ZoopPreviewCache activePreviewCache;
  private ZoopLifecycleState state;

  internal ZoopController(ZoopPreviewValidator previewValidator, ZoopPlacementUpdateGate placementUpdateGate)
  {
    this.placementUpdateGate = placementUpdateGate;
    previewCoordinator = new ZoopPreviewCoordinator(previewValidator);

    buildScheduler = new ZoopBuildScheduler(previewCoordinator, this);
    waypointManager = new ZoopWaypointManager(previewCoordinator, this);
  }

  public int PreviewCount => activeDraft?.PreviewCount ?? 0;
  public bool AllowPlacementUpdate => placementUpdateGate.AllowPlacementUpdate;
  public bool IsBuildExecuting => buildScheduler.IsBuildExecuting;
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
      buildScheduler.CancelBuildExecution();
      return;
    }

    ZoopLog.Debug($"[Lifecycle] Canceling zoop from state {state}.");
    ResetSession(restoreCursorVisibility: true, cancelPendingBuild: true);
  }

  public void ConfirmZoop(InventoryManager inventoryManager) => buildScheduler.ConfirmZoop(inventoryManager);

  public void AddWaypoint() => waypointManager.AddWaypoint();

  public void RemoveLastWaypoint() => waypointManager.RemoveLastWaypoint();

  public void CancelBuildExecution() => buildScheduler.CancelBuildExecution();

  ZoopDraft IZoopController.ActiveDraft => activeDraft;
  ZoopPreviewCache IZoopController.ActivePreviewCache => activePreviewCache;
  void IZoopController.EnterPendingBuildState() => StopPreviewLoop(ZoopLifecycleState.PendingBuild);
  void IZoopController.ResetSessionForBuild() => ResetSession(restoreCursorVisibility: false, cancelPendingBuild: false);
  void IZoopController.ResumePreviewing(InventoryManager inventoryManager) => ResumePreviewing(inventoryManager);

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
    ZoopLog.Debug(
      $"[Lifecycle] Resetting zoop session from state {state}. RestoreCursor={restoreCursorVisibility}, CancelPendingBuild={cancelPendingBuild}.");
    if (cancelPendingBuild)
    {
      buildScheduler.CancelPendingBuild();
    }
    else
    {
      buildScheduler.ClearPendingBuildState();
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
