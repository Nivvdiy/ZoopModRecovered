using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;
using ZoopMod.Zoop.Logging;
using ZoopMod.Zoop.Placement;

namespace ZoopMod.Zoop.Core;

/// <summary>
/// Owns the zoop build pipeline: scheduling the confirm delay, capturing the build plan,
/// issuing the frame-sliced build execution, and canceling in-flight coroutines.
/// </summary>
internal sealed class ZoopBuildScheduler
{
  private const float ConfirmDraftDelaySeconds = 0.100f;

  private static readonly MethodInfo WaitUntilDoneMethod = typeof(InventoryManager).GetMethod("WaitUntilDone",
    BindingFlags.NonPublic | BindingFlags.Instance, null,
    [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
    null);

  private readonly ZoopPreviewCoordinator previewCoordinator;
  private readonly IZoopController host;

  private Coroutine pendingBuildCoroutine;
  private InventoryManager pendingBuildOwner;
  private ZoopBuildPlan pendingBuildPlan;
  private Coroutine buildExecutionCoroutine;
  private InventoryManager buildExecutionOwner;

  internal ZoopBuildScheduler(ZoopPreviewCoordinator previewCoordinator, IZoopController host)
  {
    this.previewCoordinator = previewCoordinator;
    this.host = host;
  }

  public bool IsBuildExecuting => buildExecutionCoroutine != null;

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
    var draft = host.ActiveDraft;
    var isPreviewing = host.IsPreviewing;
    if (!isPreviewing || draft == null)
    {
      ZoopLog.Debug($"[Build] Confirm ignored. IsPreviewing={isPreviewing}, DraftPresent={draft != null}.");
      return;
    }

    if (!previewCoordinator.TryFinalizePreview(draft, host.ActivePreviewCache, inventoryManager))
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
      host.CancelZoop();
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

  private void BuildPendingZoop(InventoryManager inventoryManager)
  {
    var buildPlan = ConsumePendingBuildPlan();
    if (buildPlan == null)
    {
      ZoopLog.Debug("[Build] Build callback ignored because no pending build plan remained.");
      return;
    }

    ZoopLog.Debug($"[Build] Executing zoop build with {buildPlan.Count} piece(s).");
    host.ResetSessionForBuild();

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
      var buildIndex =
        ZoopConstructableResolver.ResolveBuildIndex(draft, inventoryManager, previewStructure, structureIndex);
      if (buildIndex < 0)
      {
        ZoopLog.Error(
          $"[Build] Unable to resolve build index for {previewStructure.PrefabName}; skipping zoop placement.");
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

  public void CancelPendingBuild()
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

  internal void ClearPendingBuildState()
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
    pendingBuildPlan = buildPlan;
    host.EnterPendingBuildState();
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
      ZoopLog.Debug($"[Build] Scheduled native delayed build for {host.ActiveDraft?.PreviewCount ?? 0} preview piece(s).");
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
    if (pendingBuildPlan == null)
    {
      yield break;
    }

    yield return null;

    if (pendingBuildPlan == null)
    {
      yield break;
    }

    if (InventoryManager.ConstructionCursor == null)
    {
      ZoopLog.Debug("[Build] Construction cursor vanished before native delayed build could start; resuming preview.");
      var owner = pendingBuildOwner;
      ClearPendingBuildState();
      host.ResumePreviewing(owner);
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
      host.ResumePreviewing(owner);
      yield break;
    }

    yield return waitRoutine;

    // TODO Restore cursor here?

    if (pendingBuildPlan == null)
    {
      yield break;
    }

    ZoopLog.Debug("[Build] Delayed zoop build was interrupted before completion; resuming zoop preview.");
    var inventoryManagerAfterWait = pendingBuildOwner;
    ClearPendingBuildState();
    host.ResumePreviewing(inventoryManagerAfterWait);
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

  private static Assets.Scripts.ICreativeSpawnable ResolveSpawnPrefabForBuild(ZoopDraft draft,
    InventoryManager inventoryManager,
    int buildIndex, Structure previewStructure)
  {
    if (InventoryManager.IsAuthoringMode && draft.Session.SpawnPrefab != null)
    {
      return draft.Session.SpawnPrefab;
    }

    return ZoopConstructableResolver.GetConstructableForBuildIndex(inventoryManager, buildIndex) ?? previewStructure;
  }
}
