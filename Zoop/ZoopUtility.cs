using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using Objects.Structures;
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
    if (!IsAllowed(InventoryManager.ConstructionCursor))
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

  /// <summary>
  /// Updates small-grid zoop previews for the current cursor position.
  /// </summary>
  private static async UniTask ZoopSmallGrid(InventoryManager inventoryManager, Vector3 currentPos,
    List<ZoopSegment> zoops)
  {
    var plan = ZoopPathPlanner.BuildSmallGridPlan(Session.Waypoints, currentPos);
    zoops.Clear();
    zoops.AddRange(plan.Segments);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    var supportsCornerVariant =
      SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables,
        inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
    BuildSmallStructureList(inventoryManager, zoops, supportsCornerVariant);

    if (PreviewPieceCount <= 0)
    {
      return;
    }

    PositionSmallGridStructures(inventoryManager, zoops, supportsCornerVariant, plan.IsSinglePlacement);
  }

  /// <summary>
  /// Positions the active small-grid preview structures along the zoop segments.
  /// </summary>
  private static void PositionSmallGridStructures(InventoryManager inventoryManager, List<ZoopSegment> zoops,
    bool supportsCornerVariant, bool singleItem)
  {
    var structureCounter = 0;
    var lastDirection = ZoopDirection.none;
    var occupiedCells = new HashSet<Vector3Int>();

    for (var segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++)
    {
      var segment = zoops[segmentIndex];
      float xOffset = 0;
      float yOffset = 0;
      float zOffset = 0;
      var startPos = Session.Waypoints[segmentIndex];
      for (var directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
      {
        if (structureCounter == PreviewPieceCount)
        {
          break;
        }

        var zoopDirection = segment.Directions[directionIndex];
        var increasing = ZoopPathPlanner.GetIncreasingForDirection(zoopDirection, segment);
        var zoopCounter = ZoopPathPlanner.GetPlacementCount(zoops.Count, segmentIndex, segment.Directions.Count,
          directionIndex, ZoopPathPlanner.GetCountForDirection(zoopDirection, segment));
        var value = ZoopPathPlanner.GetDirectionalPlacementValue(increasing,
          InventoryManager.ConstructionCursor is SmallGrid, spacing);

        for (var zi = 0; zi < zoopCounter; zi++)
        {
          if (structureCounter == PreviewPieceCount)
          {
            break;
          }

          ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, zi * value);

          var increasingFrom = ZoopPathPlanner.GetIncreasingFromPreviousDirection(zoops, segment, segmentIndex,
            directionIndex, zi,
            lastDirection);
          ApplySmallGridRotation(structureCounter, supportsCornerVariant, singleItem, segmentIndex, directionIndex, zi,
            lastDirection, zoopDirection, increasingFrom, increasing);

          lastDirection = zoopDirection;

          var offset = new Vector3(xOffset, yOffset, zOffset);
          var previewPosition = startPos + offset;
          var previewStructure = GetPreviewStructure(structureCounter);
          previewStructure.GameObject.SetActive(true);
          previewStructure.ThingTransformPosition = previewPosition;
          previewStructure.Position = previewPosition;
          if (!ZoopMod.CFree)
          {
            // Small-grid zoops cannot safely revisit the same cell: cables can overlap incorrectly and chutes
            // do not have a valid intersection piece at all.
            var cellKey = GetSmallGridCellKey(previewPosition);
            var revisitsExistingZoopCell = occupiedCells.Contains(cellKey);
            occupiedCells.Add(cellKey);
            HasError = HasError || revisitsExistingZoopCell;
            HasError = HasError || !CanConstructSmallCell(inventoryManager, previewStructure, structureCounter);
          }

          structureCounter++;
          if (zi == zoopCounter - 1)
          {
            ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, (zi + 1) * value);
          }
        }
      }
    }
  }

  /// <summary>
  /// Applies the correct rotation to a small-grid preview structure.
  /// </summary>
  private static void ApplySmallGridRotation(int structureCounter, bool supportsCornerVariant, bool singleItem,
    int segmentIndex, int directionIndex, int placementIndex, ZoopDirection lastDirection, ZoopDirection zoopDirection,
    bool increasingFrom, bool increasingTo)
  {
    if (!supportsCornerVariant)
    {
      return;
    }

    var isSegmentTurnStart = (directionIndex > 0 || (segmentIndex > 0 && directionIndex == 0)) && placementIndex == 0;
    if (isSegmentTurnStart)
    {
      if (lastDirection == zoopDirection)
      {
        SetStraightRotationSmallGrid(GetPreviewStructure(structureCounter), zoopDirection);
      }
      else
      {
        SetCornerRotation(GetPreviewStructure(structureCounter), lastDirection, increasingFrom, zoopDirection,
          increasingTo);
      }

      return;
    }

    if (!singleItem)
    {
      SetStraightRotationSmallGrid(GetPreviewStructure(structureCounter), zoopDirection);
    }
  }

  /// <summary>
  /// Updates large-grid zoop previews for the current cursor position.
  /// </summary>
  private static async UniTask ZoopBigGrid(InventoryManager inventoryManager, Vector3 currentPos)
  {
    var startPos = Session.Waypoints[0];
    var endPos = ClampWallZoopPositionToStartPlane(startPos, currentPos);

    var plane = ZoopPathPlanner.BuildBigGridPlane(startPos, endPos);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    BuildBigStructureList(inventoryManager, plane);

    var structureCounter = 0;
    if (PreviewPieceCount <= 0)
    {
      return;
    }

    float xOffset = 0;
    float yOffset = 0;
    float zOffset = 0;

    spacing = Mathf.Max(spacing, 1);

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      var zoopDirection2 = plane.Directions.direction2;
      var increasing2 = plane.Increasing.direction2;

      var value2 = ZoopPathPlanner.GetDirectionalPlacementValue(increasing2, false, spacing);
      ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection2, indexDirection2 * value2);

      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        if (structureCounter == PreviewPieceCount)
        {
          break;
        }

        var zoopDirection1 = plane.Directions.direction1;
        var increasing1 = plane.Increasing.direction1;

        var value1 = ZoopPathPlanner.GetDirectionalPlacementValue(increasing1, false, spacing);
        ZoopPathPlanner.SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection1,
          indexDirection1 * value1);

        var offset = new Vector3(xOffset, yOffset, zOffset);
        var previewPosition = startPos + offset;
        var previewStructure = GetPreviewStructure(structureCounter);
        previewStructure.GameObject.SetActive(true);
        previewStructure.ThingTransformPosition = previewPosition;
        previewStructure.Position = previewPosition;
        HasError = HasError || !CanConstructBigCell(inventoryManager, previewStructure, structureCounter);
        structureCounter++;
      }
    }
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
    if (!SupportsWaypoints())
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
    if (!SupportsWaypoints())
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

  /// <summary>
  /// Converts a small-grid position into a stable half-grid cell key.
  /// </summary>
  private static Vector3Int GetSmallGridCellKey(Vector3 position)
  {
    return new Vector3Int(
      Mathf.RoundToInt(position.x * 2f),
      Mathf.RoundToInt(position.y * 2f),
      Mathf.RoundToInt(position.z * 2f));
  }

  /// <summary>
  /// Applies a rotation to both the structure state and Unity transform.
  /// </summary>
  private static void SetStructureRotation(Structure structure, Quaternion rotation)
  {
    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
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
  /// Determines whether the current cursor type supports zooping.
  /// </summary>
  private static bool IsAllowed(Structure constructionCursor)
  {
    return constructionCursor is Pipe or Cable or Chute or Frame or Wall;
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
  /// Returns whether the active zoop type supports user-added waypoint corners.
  /// </summary>
  private static bool SupportsWaypoints()
  {
    return InventoryManager.ConstructionCursor is not Frame
           && InventoryManager.ConstructionCursor is not Wall;
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

  #region SmallGrid Methods

  /// <summary>
  /// Builds the preview structure list for a small-grid zoop path.
  /// </summary>
  private static void BuildSmallStructureList(InventoryManager inventoryManager, List<ZoopSegment> zoops,
    bool supportsCornerVariant)
  {
    PreviewFactory.ResetSmallGridPreviewList();

    var straight = 0;
    var corners = 0;
    var lastDirection = ZoopDirection.none;
    var canBuildNext = true;
    for (var segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++)
    {
      var segment = zoops[segmentIndex];
      for (var directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
      {
        var zoopDirection = segment.Directions[directionIndex];
        var zoopCounter = ZoopPathPlanner.GetCountForDirection(zoopDirection, segment);

        zoopCounter = ZoopPathPlanner.GetPlacementCount(zoops.Count, segmentIndex, segment.Directions.Count, directionIndex,
          zoopCounter);

        for (var j = 0; j < zoopCounter; j++)
        {
          if (PreviewPieceCount > 0 && (j == 0 || segmentIndex > 0) && supportsCornerVariant)
          {
            if (zoopDirection != lastDirection)
            {
              PreviewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, true, corners,
                straight, ref canBuildNext, inventoryManager,
                supportsCornerVariant); // start with corner on secondary and tertiary zoop directions
              corners++;
            }
            else
            {
              PreviewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight,
                corners, ref canBuildNext, inventoryManager, supportsCornerVariant);
              straight++;
            }
          }
          else
          {
            PreviewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight,
              corners, ref canBuildNext, inventoryManager, supportsCornerVariant);
            straight++;
          }

          lastDirection = zoopDirection;
        }
      }
    }
  }

  /// <summary>
  /// Determines whether the selected constructable family has a matching corner variant.
  /// </summary>
  private static bool SupportsCornerVariant(List<Structure> constructables, int selectedIndex)
  {
    if (constructables == null || selectedIndex < 0 || selectedIndex >= constructables.Count)
    {
      return false;
    }

    var selectedStructure = constructables[selectedIndex];
    if (selectedStructure == null)
    {
      return false;
    }

    return constructables.Any(structure =>
      IsCornerVariant(structure)
      && IsMatchingCornerFamily(selectedStructure, structure));
  }

  /// <summary>
  /// Returns whether a structure represents a corner prefab variant.
  /// </summary>
  private static bool IsCornerVariant(Structure structure)
  {
    if (structure == null)
    {
      return false;
    }

    var prefabName = structure.GetPrefabName();
    if (!string.IsNullOrEmpty(prefabName) &&
        prefabName.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      return true;
    }

    return structure.GetType().Name.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  /// <summary>
  /// Checks whether a corner structure belongs to the same family as the selected structure.
  /// </summary>
  private static bool IsMatchingCornerFamily(Structure selectedStructure, Structure cornerStructure)
  {
    return selectedStructure switch
    {
      Chute => cornerStructure is Chute,
      Cable => cornerStructure is Cable,
      Frame => cornerStructure is Frame,
      Pipe => cornerStructure is Pipe,
      _ => false
    };
  }

  #endregion

  #region BigGrid Methods

  /// <summary>
  /// Builds the preview structure list for a large-grid zoop plane.
  /// </summary>
  private static void BuildBigStructureList(InventoryManager inventoryManager, ZoopPlane plane)
  {
    PreviewFactory.ResetBigGridPreviewList();
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        PreviewFactory.AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0,
          ref canBuildNext, inventoryManager, false);
        count++;
      }
    }
  }

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

  #region Calculation Methods

  /// <summary>
  /// Rotates a small-grid straight preview to match its zoop direction.
  /// </summary>
  private static void SetStraightRotationSmallGrid(Structure structure, ZoopDirection zoopDirection)
  {
    switch (zoopDirection)
    {
      case ZoopDirection.x:
        SetStructureRotation(structure, structure is Chute
          ? SmartRotate.RotX.Rotation
          : SmartRotate.RotY.Rotation);

        break;
      case ZoopDirection.y:
        SetStructureRotation(structure, structure is Chute
          ? SmartRotate.RotZ.Rotation
          : SmartRotate.RotX.Rotation);

        break;
      case ZoopDirection.z:
        SetStructureRotation(structure, structure is Chute
          ? SmartRotate.RotY.Rotation
          : SmartRotate.RotZ.Rotation);

        break;
      case ZoopDirection.none:
      default:
        throw new ArgumentOutOfRangeException(nameof(zoopDirection), zoopDirection, null);
    }
  }

  /// <summary>
  /// Rotates a corner preview so it connects the previous and next zoop directions.
  /// </summary>
  private static void SetCornerRotation(Structure structure, ZoopDirection zoopDirectionFrom, bool increasingFrom,
    ZoopDirection zoopDirectionTo, bool increasingTo)
  {
    var xOffset = 0.0f;
    var yOffset = 0.0f;
    var zOffset = 0.0f;
    if (structure.GetPrefabName().Equals("StructureCableCorner"))
    {
      xOffset = 180.0f;
    }

    if (structure.GetPrefabName().Equals("StructureChuteCorner"))
    {
      xOffset = -90.0f;
      switch (zoopDirectionTo)
      {
        case ZoopDirection.z when zoopDirectionFrom == ZoopDirection.x:
          yOffset = increasingTo ? -90.0f : 90f;
          break;
        //good
        case ZoopDirection.x when zoopDirectionFrom == ZoopDirection.z:
          yOffset = increasingFrom ? 90.0f : -90f;
          break;
        default:
          yOffset = 180.0f;
          break;
      }
    }

    SetStructureRotation(structure,
      ZoopUtils.GetCornerRotation(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo, xOffset, yOffset,
        zOffset));
  }

  #endregion
}
