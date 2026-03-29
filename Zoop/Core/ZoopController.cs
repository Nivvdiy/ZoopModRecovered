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
internal sealed class ZoopController
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

  private readonly ZoopSession session;
  private readonly ZoopPreviewFactory previewFactory;
  private readonly ZoopPreviewColorizer previewColorizer;
  private readonly ZoopSmallGridCoordinator smallGridCoordinator;
  private readonly ZoopBigGridCoordinator bigGridCoordinator;
  private readonly ZoopConstructableResolver constructableResolver;
  private ZoopLifecycleState state;
  private Coroutine pendingBuildCoroutine;
  private InventoryManager pendingBuildOwner;
  private ZoopBuildPlan pendingBuildPlan;
  private int currentPreviewLoopId;
  private int nextPreviewLoopId;

  public ZoopController(ZoopSession session, ZoopPreviewFactory previewFactory, ZoopPreviewValidator previewValidator,
    ZoopConstructableResolver constructableResolver)
  {
    this.session = session;
    this.previewFactory = previewFactory;
    this.constructableResolver = constructableResolver;
    previewColorizer = new ZoopPreviewColorizer(session, () => LineColor);
    smallGridCoordinator = new ZoopSmallGridCoordinator(session, previewFactory, previewValidator);
    bigGridCoordinator = new ZoopBigGridCoordinator(session, previewFactory, previewValidator);
  }

  public int PreviewCount => session.PreviewCount;
  public bool HasError => session.HasError;
  public bool AllowPlacementUpdate => session.AllowPlacementUpdate;
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
    if (!IsPreviewing || HasError)
    {
      return;
    }

    var buildPlan = CaptureBuildPlan(inventoryManager);
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
    if (!IsPreviewing || !ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    var currentPos = GetCurrentMouseGridPosition();
    var lastWaypoint = session.Waypoints[session.Waypoints.Count - 1];
    if (currentPos.HasValue && !IsSameZoopPosition(lastWaypoint, currentPos.Value))
    {
      if (session.PreviewCount > 0 &&
          IsSameZoopPosition(session.PreviewPieces[session.PreviewCount - 1].Structure.Position, currentPos.Value))
      {
        session.Waypoints.Add(currentPos.Value);
      }
    }
    else if (currentPos.HasValue && IsSameZoopPosition(lastWaypoint, currentPos.Value))
    {
      // TODO show message to user that waypoint is already added
    }
  }

  public void RemoveLastWaypoint()
  {
    if (!IsPreviewing || !ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    if (session.Waypoints.Count > 1)
    {
      session.Waypoints.RemoveAt(session.Waypoints.Count - 1);
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

    session.ResetActiveZoopState();
    session.Waypoints.Clear();
    session.ZoopSpawnPrefab = InventoryManager.SpawnPrefab;
    if (InventoryManager.ConstructionCursor != null)
    {
      var selectedConstructable = GetSelectedConstructable(inventoryManager);
      using (session.BeginPlacementUpdateScope())
      {
        if (selectedConstructable != null)
        {
          InventoryManager.UpdatePlacement(selectedConstructable);
        }
      }

      session.ZoopStartRotation = InventoryManager.ConstructionCursor.transform.rotation;
      session.ZoopStartWallNormal = InventoryManager.ConstructionCursor is Wall
        ? GetCardinalAxis(InventoryManager.ConstructionCursor.transform.forward)
        : Vector3.zero;

      var startPos = GetCurrentMouseGridPosition();
      if (startPos.HasValue)
      {
        session.Waypoints.Add(startPos.Value);
      }
    }

    if (session.Waypoints.Count <= 0)
    {
      ResetSession(restoreCursorVisibility: true, cancelPendingBuild: true);
      return;
    }

    state = ZoopLifecycleState.Previewing;
    var cts = new CancellationTokenSource();
    session.CancellationSource = cts;
    var previewLoopId = System.Threading.Interlocked.Increment(ref nextPreviewLoopId);
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
        if (previewLoopId == currentPreviewLoopId && ReferenceEquals(session.CancellationSource, cts))
        {
          session.CancellationSource = null;
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

  private ZoopBuildPlan CaptureBuildPlan(InventoryManager inventoryManager)
  {
    var pieces = new List<ZoopBuildPiece>(session.PreviewCount);
    for (var structureIndex = 0; structureIndex < session.PreviewCount; structureIndex++)
    {
      var previewPiece = session.PreviewPieces[structureIndex];
      var previewStructure = previewPiece.Structure;
      var buildIndex = constructableResolver.ResolveBuildIndex(inventoryManager, previewStructure, structureIndex);
      if (buildIndex < 0)
      {
        ZoopLog.Error($"[Build] Unable to resolve build index for {previewStructure.PrefabName}; skipping zoop placement.");
        continue;
      }

      var spawnPrefab = ResolveSpawnPrefabForBuild(inventoryManager, buildIndex, previewStructure);
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
        if (session.Waypoints.Count > 0)
        {
          var currentPos = GetCurrentMouseGridPosition();
          if (currentPos.HasValue)
          {
            session.HasError = false;

            if (IsZoopingSmallGrid())
            {
              await smallGridCoordinator.UpdatePreview(inventoryManager, currentPos.Value, segments, DefaultSpacing);
            }
            else if (IsZoopingBigGrid())
            {
              await bigGridCoordinator.UpdatePreview(inventoryManager, currentPos.Value, DefaultSpacing);
            }

            foreach (var previewPiece in session.PreviewPieces)
            {
              previewColorizer.ApplyColor(inventoryManager, previewPiece.Structure, session.HasError);
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

  private Assets.Scripts.ICreativeSpawnable ResolveSpawnPrefabForBuild(InventoryManager inventoryManager, int buildIndex,
    Structure previewStructure)
  {
    if (InventoryManager.IsAuthoringMode && session.ZoopSpawnPrefab != null)
    {
      return session.ZoopSpawnPrefab;
    }

    return constructableResolver.GetConstructableForBuildIndex(inventoryManager, buildIndex) ?? previewStructure;
  }

  private void StopPreviewLoop(ZoopLifecycleState nextState)
  {
    currentPreviewLoopId = 0;
    var cancellationSource = session.CancellationSource;
    session.CancellationSource = null;
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
    previewFactory.ClearStructureCache();
    session.ResetActiveZoopState();

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
