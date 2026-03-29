using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZoopMod.Zoop;

public static class ZoopUtility
{
  #region Fields

  private static readonly ZoopSession Session = new();
  private static readonly ZoopPreviewFactory PreviewFactory = new(Session);
  private static readonly ZoopPreviewValidator PreviewValidator =
    new(ZoopConstructableResolver.ResolveBuildIndex, ZoopConstructableResolver.GetConstructableForBuildIndex,
      allowPlacementUpdate => AllowPlacementUpdate = allowPlacementUpdate);
  private static readonly ZoopPreviewColorizer PreviewColorizer = new(Session, () => LineColor);
  private static readonly ZoopSmallGridCoordinator SmallGridCoordinator =
    new(Session, PreviewFactory, CanConstructSmallCell, hasError => HasError = HasError || hasError);
  private static readonly ZoopBigGridCoordinator BigGridCoordinator =
    new(Session, PreviewFactory, CanConstructBigCell, hasError => HasError = HasError || hasError);

  public static int PreviewCount => Session.PreviewCount;
  public static bool HasError { get => Session.HasError; private set => Session.HasError = value; }
  public static Coroutine ActionCoroutine { get => Session.ActionCoroutine; private set => Session.ActionCoroutine = value; }
  public static bool AllowPlacementUpdate
  {
    get => Session.AllowPlacementUpdate;
    private set => Session.AllowPlacementUpdate = value;
  }

  public static bool IsZoopKeyPressed { get; set; }

  public static bool IsZooping { get; private set; }
  private static int spacing = 1;

  public static Color LineColor { get; set; } = Color.green;

  private static int PreviewPieceCount => Session.PreviewCount;

  #endregion

  #region Common Methods

  /// <summary>
  /// Starts a new zoop preview from the current construction cursor.
  /// </summary>
  public static void StartZoop(InventoryManager inventoryManager)
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

    Session.Waypoints.Clear();
    Session.ZoopSpawnPrefab = InventoryManager.SpawnPrefab;
    if (InventoryManager.ConstructionCursor != null)
    {
      var selectedConstructable = GetSelectedConstructable(inventoryManager);
      AllowPlacementUpdate = true;
      try
      {
        if (selectedConstructable != null)
        {
          InventoryManager.UpdatePlacement(selectedConstructable);
        }
      }
      finally
      {
        AllowPlacementUpdate = false;
      }

      Session.ZoopStartRotation = InventoryManager.ConstructionCursor.transform.rotation;
      Session.ZoopStartWallNormal = InventoryManager.ConstructionCursor is Wall
        ? GetCardinalAxis(InventoryManager.ConstructionCursor.transform.forward)
        : Vector3.zero;

      var startPos = GetCurrentMouseGridPosition();
      if (startPos.HasValue)
      {
        Session.Waypoints.Add(startPos.Value); // Add start position as the first waypoint
      }
    }

    if (Session.Waypoints.Count <= 0)
    {
      IsZooping = false;
      return;
    }

    var cts = new CancellationTokenSource();
    Session.CancellationSource = cts;
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
        Session.CancellationSource = null;
        IsZooping = false;
      }
    }, cancellationToken: ct);
  }

  /// <summary>
  /// Cancels the current zoop preview and clears its temporary state.
  /// </summary>
  public static void CancelZoop()
  {
    IsZooping = false;
    CancelPendingBuild();
    if (Session.CancellationSource != null)
    {
      Session.CancellationSource.Cancel();
      Session.CancellationSource = null;
      PreviewFactory.ClearStructureCache();
      Session.ResetActiveZoopState(); //try to reset a list of structures for single piece placing
    }

    Session.ZoopSpawnPrefab = null;

    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(true);
    }
  }

  /// <summary>
  /// Stores the pending build coroutine so it can be cancelled or replaced later.
  /// </summary>
  public static void SetPendingBuild(InventoryManager inventoryManager, Coroutine coroutine)
  {
    CancelPendingBuild();
    ActionCoroutine = coroutine;
    Session.ActionCoroutineOwner = inventoryManager;
  }

  /// <summary>
  /// Stops the currently tracked build coroutine if one is running.
  /// </summary>
  private static void CancelPendingBuild()
  {
    if (Session.ActionCoroutineOwner != null && ActionCoroutine != null)
    {
      Session.ActionCoroutineOwner.StopCoroutine(ActionCoroutine);
    }

    ClearPendingBuild();
  }

  /// <summary>
  /// Clears the tracked pending build coroutine references.
  /// </summary>
  private static void ClearPendingBuild()
  {
    Session.ClearPendingBuildState();
  }

  private static Structure GetPreviewStructure(int index)
  {
    return Session.PreviewPieces[index].Structure;
  }

  /// <summary>
  /// Continuously updates zoop preview structures until the operation is cancelled.
  /// </summary>
  private static async UniTask ZoopAsync(CancellationToken cancellationToken, InventoryManager inventoryManager)
  {
    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    List<ZoopSegment> zoops = [];
    if (InventoryManager.ConstructionCursor != null)
    {
      InventoryManager.ConstructionCursor.gameObject.SetActive(false);
    }

    while (cancellationToken is { IsCancellationRequested: false })
    {
      try
      {
        if (Session.Waypoints.Count > 0)
        {
          var currentPos = GetCurrentMouseGridPosition();
          if (currentPos.HasValue)
          {
            HasError = false;

            if (IsZoopingSmallGrid())
            {
              await ZoopSmallGrid(inventoryManager, currentPos.Value, zoops);
            }
            else if (IsZoopingBigGrid())
            {
              await ZoopBigGrid(inventoryManager, currentPos.Value);
            }

            foreach (var previewPiece in Session.PreviewPieces)
            {
              PreviewColorizer.ApplyColor(inventoryManager, previewPiece.Structure, HasError);
            }
          }
        }

        await UniTask.Delay(100, DelayType.Realtime, cancellationToken: cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // Cancellation is expected, dont log anything
        break;
      }
      catch (Exception e)
      {
        ZoopMod.Log(e.ToString(), ZoopMod.Logs.error);
      }
    }
  }

  private static UniTask ZoopSmallGrid(InventoryManager inventoryManager, Vector3 currentPos, List<ZoopSegment> zoops)
  {
    return SmallGridCoordinator.UpdatePreview(inventoryManager, currentPos, zoops, spacing);
  }

  private static UniTask ZoopBigGrid(InventoryManager inventoryManager, Vector3 currentPos)
  {
    return BigGridCoordinator.UpdatePreview(inventoryManager, currentPos, spacing, ClampWallZoopPositionToStartPlane);
  }

  /// <summary>
  /// Places all previewed structures into the world and ends the active zoop.
  /// </summary>
  public static void BuildZoop(InventoryManager inventoryManager)
  {
    ClearPendingBuild();
    ZoopBuildExecutor.BuildAll(inventoryManager, Session);

    inventoryManager.CancelPlacement();
    CancelZoop();
  }

  /// <summary>
  /// Adds the current preview position as an additional zoop waypoint when valid.
  /// </summary>
  public static void AddWaypoint()
  {
    if (!ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    var currentPos = GetCurrentMouseGridPosition();
    if (currentPos.HasValue && !IsSameZoopPosition(Session.Waypoints.Last(), currentPos.Value))
    {
      if (PreviewPieceCount > 0 && IsSameZoopPosition(GetPreviewStructure(PreviewPieceCount - 1).Position, currentPos.Value))
      {
        Session.Waypoints.Add(currentPos.Value);
      }
    }
    else if (currentPos.HasValue && IsSameZoopPosition(Session.Waypoints.Last(), currentPos.Value))
    {
      //TODO show message to user that waypoint is already added
    }
  }

  /// <summary>
  /// Removes the most recently added zoop waypoint when possible.
  /// </summary>
  public static void RemoveLastWaypoint()
  {
    if (!ZoopConstructableRules.SupportsWaypoints(InventoryManager.ConstructionCursor))
    {
      return;
    }

    if (Session.Waypoints.Count > 1)
    {
      Session.Waypoints.RemoveAt(Session.Waypoints.Count - 1);
    }
  }

  /// <summary>
  /// Returns the current construction cursor position snapped to the zoop grid.
  /// </summary>
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

    var cursorHitPoint = InventoryManager.ConstructionCursor.GetLocalGrid().ToVector3();
    return cursorHitPoint;
  }

  /// <summary>
  /// Locks wall zoops to the plane defined by the starting wall face.
  /// </summary>
  private static Vector3 ClampWallZoopPositionToStartPlane(Vector3 startPos, Vector3 targetPos)
  {
    if (InventoryManager.ConstructionCursor is not Wall || Session.ZoopStartWallNormal == Vector3.zero)
    {
      return targetPos;
    }

    if (Mathf.Abs(Session.ZoopStartWallNormal.x) > 0.99f)
    {
      targetPos.x = startPos.x;
    }
    else if (Mathf.Abs(Session.ZoopStartWallNormal.y) > 0.99f)
    {
      targetPos.y = startPos.y;
    }
    else
    {
      targetPos.z = startPos.z;
    }

    return targetPos;
  }

  /// <summary>
  /// Compares two zoop positions using a small tolerance for floating point drift.
  /// </summary>
  private static bool IsSameZoopPosition(Vector3 first, Vector3 second)
  {
    // Mounted wall previews use world positions, so tiny float drift can appear between cursor, waypoint, and preview values.
    return Vector3.SqrMagnitude(first - second) < ZoopPreviewColorizer.PositionToleranceSqr;
  }

  #endregion

  #region Conditional Methods

  /// <summary>
  /// Returns the constructable currently selected in the construction panel.
  /// </summary>
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

  /// <summary>
  /// Returns whether the active zoop cursor uses the small grid rules.
  /// </summary>
  private static bool IsZoopingSmallGrid()
  {
    return InventoryManager.ConstructionCursor is SmallGrid;
  }

  /// <summary>
  /// Returns whether the active zoop cursor uses the large grid rules.
  /// </summary>
  private static bool IsZoopingBigGrid()
  {
    return InventoryManager.ConstructionCursor is LargeStructure;
  }

  /// <summary>
  /// Checks whether a small-grid preview structure can be built in its current cell.
  /// </summary>
  private static bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return PreviewValidator.CanConstructSmallCell(Session, inventoryManager, structure, structureIndex);
  }

  /// <summary>
  /// Checks whether a large-grid preview structure can be built in its current cell.
  /// </summary>
  private static bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return PreviewValidator.CanConstructBigCell(Session, inventoryManager, structure, structureIndex);
  }

  #endregion

  #region Helper Methods

  /// <summary>
  /// Reduces a vector to its dominant cardinal axis.
  /// </summary>
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

  #endregion
}
