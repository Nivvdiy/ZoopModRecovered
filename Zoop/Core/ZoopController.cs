using System;
using System.Collections.Generic;
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
  private const int DefaultSpacing = 1;

  private readonly ZoopSession session;
  private readonly ZoopPreviewFactory previewFactory;
  private readonly ZoopPreviewColorizer previewColorizer;
  private readonly ZoopSmallGridCoordinator smallGridCoordinator;
  private readonly ZoopBigGridCoordinator bigGridCoordinator;

  public ZoopController(ZoopSession session, ZoopPreviewFactory previewFactory, ZoopPreviewValidator previewValidator)
  {
    this.session = session;
    this.previewFactory = previewFactory;
    previewColorizer = new ZoopPreviewColorizer(session, () => LineColor);
    smallGridCoordinator = new ZoopSmallGridCoordinator(session, previewFactory, previewValidator);
    bigGridCoordinator = new ZoopBigGridCoordinator(session, previewFactory, previewValidator);
  }

  public int PreviewCount => session.PreviewCount;
  public bool HasError => session.HasError;
  public bool AllowPlacementUpdate => session.AllowPlacementUpdate;
  public bool IsZoopKeyPressed { get; set; }
  public bool IsZooping { get; private set; }
  public Color LineColor { get; set; } = Color.green;

  public void StartZoop(InventoryManager inventoryManager)
  {
    if (!ZoopConstructableRules.IsAllowed(InventoryManager.ConstructionCursor))
    {
      return;
    }

    if (IsZooping)
    {
      CancelZoop();
      return;
    }

    IsZooping = true;

    session.Waypoints.Clear();
    session.ZoopSpawnPrefab = InventoryManager.SpawnPrefab;
    if (InventoryManager.ConstructionCursor != null)
    {
      var selectedConstructable = GetSelectedConstructable(inventoryManager);
      session.AllowPlacementUpdate = true;
      try
      {
        if (selectedConstructable != null)
        {
          InventoryManager.UpdatePlacement(selectedConstructable);
        }
      }
      finally
      {
        session.AllowPlacementUpdate = false;
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
      IsZooping = false;
      return;
    }

    var cts = new CancellationTokenSource();
    session.CancellationSource = cts;
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
        session.CancellationSource = null;
        IsZooping = false;
      }
    }, cancellationToken: ct);
  }

  public void CancelZoop()
  {
    IsZooping = false;
    CancelPendingBuild();
    if (session.CancellationSource != null)
    {
      session.CancellationSource.Cancel();
      session.CancellationSource = null;
      previewFactory.ClearStructureCache();
      session.ResetActiveZoopState();
    }

    session.ZoopSpawnPrefab = null;

    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(true);
    }
  }

  public void SetPendingBuild(InventoryManager inventoryManager, Coroutine coroutine)
  {
    CancelPendingBuild();
    session.ActionCoroutine = coroutine;
    session.ActionCoroutineOwner = inventoryManager;
  }

  public void BuildZoop(InventoryManager inventoryManager)
  {
    session.ClearPendingBuildState();
    ZoopBuildExecutor.BuildAll(inventoryManager, session);

    inventoryManager.CancelPlacement();
    CancelZoop();
  }

  public void AddWaypoint()
  {
    if (!ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
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
    if (!ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    if (session.Waypoints.Count > 1)
    {
      session.Waypoints.RemoveAt(session.Waypoints.Count - 1);
    }
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
    if (session.ActionCoroutineOwner != null && session.ActionCoroutine != null)
    {
      session.ActionCoroutineOwner.StopCoroutine(session.ActionCoroutine);
    }

    session.ClearPendingBuildState();
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
