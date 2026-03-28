using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using Objects.Structures;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace ZoopMod.Zoop;

public static class ZoopUtility
{
  #region Fields

  public static readonly List<Structure> Structures = [];
  private static readonly List<int> StructureBuildIndices = [];
  private static readonly List<Structure> structuresCacheStraight = [];
  private static readonly List<int> StructureCacheStraightBuildIndices = [];
  private static readonly List<Structure> structuresCacheCorner = [];
  private static readonly List<int> StructureCacheCornerBuildIndices = [];

  private static readonly List<Vector3?> Waypoints = [];

  public static bool HasError { get; private set; }
  public static Coroutine ActionCoroutine { get; private set; }
  public static bool AllowPlacementUpdate { get; private set; }
  private static CancellationTokenSource _zoopCancellationSource;
  private static InventoryManager _actionCoroutineOwner;
  private static ICreativeSpawnable _zoopSpawnPrefab;
  private static Quaternion _zoopStartRotation = Quaternion.identity;
  private static Vector3 _zoopStartWallNormal = Vector3.zero;
  private const float PositionToleranceSqr = 0.0001f;

  private readonly struct ValidationCursorState
  {
    public ValidationCursorState(int originalBuildIndex, int validationBuildIndex, bool needsCursorSwap,
      Vector3 originalCursorPosition, Vector3 originalCursorLocalPosition, Quaternion originalCursorRotation,
      Quaternion originalCursorLocalRotation)
    {
      OriginalBuildIndex = originalBuildIndex;
      ValidationBuildIndex = validationBuildIndex;
      NeedsCursorSwap = needsCursorSwap;
      OriginalCursorPosition = originalCursorPosition;
      OriginalCursorLocalPosition = originalCursorLocalPosition;
      OriginalCursorRotation = originalCursorRotation;
      OriginalCursorLocalRotation = originalCursorLocalRotation;
    }

    public int OriginalBuildIndex { get; }
    public int ValidationBuildIndex { get; }
    public bool NeedsCursorSwap { get; }
    public Vector3 OriginalCursorPosition { get; }
    public Vector3 OriginalCursorLocalPosition { get; }
    public Quaternion OriginalCursorRotation { get; }
    public Quaternion OriginalCursorLocalRotation { get; }
  }

  private static readonly FieldInfo UsePrimaryPositionField =
    typeof(InventoryManager).GetField("_usePrimaryPosition", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly FieldInfo UsePrimaryRotationField =
    typeof(InventoryManager).GetField("_usePrimaryRotation", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly MethodInfo UsePrimaryCompleteMethod = typeof(InventoryManager).GetMethod("UsePrimaryComplete",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

  public static bool IsZoopKeyPressed { get; set; }

  public static bool IsZooping { get; private set; }
  private static int spacing = 1;

  public static Color LineColor { get; set; } = Color.green;
  private static Color ErrorColor { get; } = Color.red;
  private static readonly Color WaypointColor = Color.blue;
  private static readonly Color StartColor = Color.magenta;

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

    Waypoints.Clear();
    _zoopSpawnPrefab = InventoryManager.SpawnPrefab;
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

      _zoopStartRotation = InventoryManager.ConstructionCursor.transform.rotation;
      _zoopStartWallNormal = InventoryManager.ConstructionCursor is Wall
        ? GetCardinalAxis(InventoryManager.ConstructionCursor.transform.forward)
        : Vector3.zero;

      var startPos = GetCurrentMouseGridPosition();
      if (startPos.HasValue)
      {
        Waypoints.Add(startPos); // Add start position as the first waypoint
      }
    }

    if (Waypoints.Count <= 0)
    {
      IsZooping = false;
      return;
    }

    var cts = new CancellationTokenSource();
    _zoopCancellationSource = cts;
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
        _zoopCancellationSource = null;
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
    if (_zoopCancellationSource != null)
    {
      _zoopCancellationSource.Cancel();
      _zoopCancellationSource = null;
      ClearStructureCache();
      Structures.Clear(); //try to reset a list of structures for single piece placing
      StructureBuildIndices.Clear();
      Waypoints.Clear();
      _zoopStartRotation = Quaternion.identity;
      _zoopStartWallNormal = Vector3.zero;
    }

    _zoopSpawnPrefab = null;

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
    _actionCoroutineOwner = inventoryManager;
  }

  /// <summary>
  /// Stops the currently tracked build coroutine if one is running.
  /// </summary>
  private static void CancelPendingBuild()
  {
    if (_actionCoroutineOwner != null && ActionCoroutine != null)
    {
      _actionCoroutineOwner.StopCoroutine(ActionCoroutine);
    }

    ClearPendingBuild();
  }

  /// <summary>
  /// Clears the tracked pending build coroutine references.
  /// </summary>
  private static void ClearPendingBuild()
  {
    ActionCoroutine = null;
    _actionCoroutineOwner = null;
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
        if (Waypoints.Count > 0)
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

            foreach (var structure in Structures)
            {
              SetColor(inventoryManager, structure, HasError);
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
    var singleItem = BuildSmallGridSegments(currentPos, zoops);

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    var supportsCornerVariant =
      SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables,
        inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
    BuildSmallStructureList(inventoryManager, zoops, supportsCornerVariant);

    if (Structures.Count <= 0)
    {
      return;
    }

    PositionSmallGridStructures(inventoryManager, zoops, supportsCornerVariant, singleItem);
  }

  /// <summary>
  /// Builds the small-grid zoop segments for the current waypoint path.
  /// </summary>
  private static bool BuildSmallGridSegments(Vector3 currentPos, List<ZoopSegment> zoops)
  {
    zoops.Clear();

    var singleItem = true;
    for (var wpIndex = 0; wpIndex < Waypoints.Count; wpIndex++)
    {
      var startPos = Waypoints[wpIndex].Value;
      var endPos = wpIndex < Waypoints.Count - 1 ? Waypoints[wpIndex + 1].Value : currentPos;

      var segment = new ZoopSegment();
      CalculateZoopSegments(startPos, endPos, segment);

      singleItem = IsSameZoopPosition(startPos, endPos);
      if (singleItem)
      {
        segment.CountX = 1 + (int)(Math.Abs(startPos.x - endPos.x) * 2);
        segment.IncreasingX = startPos.x < endPos.x;
        segment.Directions.Add(ZoopDirection.x); // unused for single item
      }

      zoops.Add(segment);
    }

    return singleItem;
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
      var startPos = Waypoints[segmentIndex].Value;
      for (var directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
      {
        if (structureCounter == Structures.Count)
        {
          break;
        }

        var zoopDirection = segment.Directions[directionIndex];
        var increasing = GetIncreasingForDirection(zoopDirection, segment);
        var zoopCounter = GetPlacementCount(zoops.Count, segmentIndex, segment.Directions.Count, directionIndex,
          GetCountForDirection(zoopDirection, segment));
        var value = GetDirectionalPlacementValue(increasing);

        for (var zi = 0; zi < zoopCounter; zi++)
        {
          if (structureCounter == Structures.Count)
          {
            break;
          }

          SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, zi * value);

          var increasingFrom = GetIncreasingFromPreviousDirection(zoops, segment, segmentIndex, directionIndex, zi,
            lastDirection);
          ApplySmallGridRotation(structureCounter, supportsCornerVariant, singleItem, segmentIndex, directionIndex, zi,
            lastDirection, zoopDirection, increasingFrom, increasing);

          lastDirection = zoopDirection;

          var offset = new Vector3(xOffset, yOffset, zOffset);
          var previewPosition = startPos + offset;
          Structures[structureCounter].GameObject.SetActive(true);
          Structures[structureCounter].ThingTransformPosition = previewPosition;
          Structures[structureCounter].Position = previewPosition;
          if (!ZoopMod.CFree)
          {
            // Small-grid zoops cannot safely revisit the same cell: cables can overlap incorrectly and chutes
            // do not have a valid intersection piece at all.
            var cellKey = GetSmallGridCellKey(previewPosition);
            var revisitsExistingZoopCell = occupiedCells.Contains(cellKey);
            occupiedCells.Add(cellKey);
            HasError = HasError || revisitsExistingZoopCell;
            HasError = HasError || !CanConstructSmallCell(inventoryManager, Structures[structureCounter], structureCounter);
          }

          structureCounter++;
          if (zi == zoopCounter - 1)
          {
            SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, (zi + 1) * value);
          }
        }
      }
    }
  }

  /// <summary>
  /// Returns the placement spacing value for the current small-grid direction.
  /// </summary>
  private static float GetDirectionalPlacementValue(bool increasing)
  {
    spacing = Mathf.Max(spacing, 1);
    var minValue = InventoryManager.ConstructionCursor is SmallGrid ? 0.5f : 2f;
    return increasing ? minValue * spacing : -(minValue * spacing);
  }

  /// <summary>
  /// Resolves the incoming direction polarity used when orienting a small-grid preview.
  /// </summary>
  private static bool GetIncreasingFromPreviousDirection(List<ZoopSegment> zoops, ZoopSegment segment, int segmentIndex,
    int directionIndex, int placementIndex, ZoopDirection lastDirection)
  {
    var increasingFrom = lastDirection != ZoopDirection.none &&
                         GetIncreasingForDirection(lastDirection, segment);
    if (segmentIndex <= 0 || directionIndex != 0 || placementIndex != 0)
    {
      return increasingFrom;
    }

    var lastSegment = zoops[segmentIndex - 1];
    return GetIncreasingForDirection(lastSegment.Directions.Last(), lastSegment);
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
        SetStraightRotationSmallGrid(Structures[structureCounter], zoopDirection);
      }
      else
      {
        SetCornerRotation(Structures[structureCounter], lastDirection, increasingFrom, zoopDirection, increasingTo);
      }

      return;
    }

    if (!singleItem)
    {
      SetStraightRotationSmallGrid(Structures[structureCounter], zoopDirection);
    }
  }

  /// <summary>
  /// Updates large-grid zoop previews for the current cursor position.
  /// </summary>
  private static async UniTask ZoopBigGrid(InventoryManager inventoryManager, Vector3 currentPos)
  {
    var startPos = Waypoints[0].Value;
    var endPos = ClampWallZoopPositionToStartPlane(startPos, currentPos);

    var plane = new ZoopPlane();
    CalculateZoopPlane(startPos, endPos, plane);

    var singleItem = IsSameZoopPosition(startPos, endPos);
    if (singleItem)
    {
      plane.Count = (direction1: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2),
        direction2: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2));
      plane.Increasing = (direction1: startPos.x < endPos.x, direction2: startPos.y < endPos.y);
      plane.Directions = (direction1: ZoopDirection.x, direction2: ZoopDirection.y);
    }

    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    BuildBigStructureList(inventoryManager, plane);

    var structureCounter = 0;
    if (Structures.Count <= 0)
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

      var value2 = increasing2 ? 2f * spacing : -(2f * spacing);
      SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection2, indexDirection2 * value2);

      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        if (structureCounter == Structures.Count)
        {
          break;
        }

        var zoopDirection1 = plane.Directions.direction1;
        var increasing1 = plane.Increasing.direction1;

        var value1 = increasing1 ? 2f * spacing : -(2f * spacing);
        SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection1, indexDirection1 * value1);

        var offset = new Vector3(xOffset, yOffset, zOffset);
        var previewPosition = startPos + offset;
        Structures[structureCounter].GameObject.SetActive(true);
        Structures[structureCounter].ThingTransformPosition = previewPosition;
        Structures[structureCounter].Position = previewPosition;
        HasError = HasError || !CanConstructBigCell(inventoryManager, Structures[structureCounter], structureCounter);
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

    for (var structureIndex = 0; structureIndex < Structures.Count; structureIndex++)
    {
      var item = Structures[structureIndex];
      PlaceStructure(inventoryManager, item, structureIndex);
    }

    inventoryManager.CancelPlacement();
    CancelZoop();
  }

  /// <summary>
  /// Adds the current preview position as an additional zoop waypoint when valid.
  /// </summary>
  public static void AddWaypoint()
  {
    if (InventoryManager.ConstructionCursor is Frame)
    {
      return;
    }

    var currentPos = GetCurrentMouseGridPosition();
    if (currentPos.HasValue && !IsSameZoopPosition(Waypoints.Last(), currentPos))
    {
      if (Structures.Count > 0 && IsSameZoopPosition(Structures.Last().Position, currentPos.Value))
      {
        Waypoints.Add(currentPos);
      }
    }
    else if (IsSameZoopPosition(Waypoints.Last(), currentPos))
    {
      //TODO show message to user that waypoint is already added
    }
  }

  /// <summary>
  /// Removes the most recently added zoop waypoint when possible.
  /// </summary>
  public static void RemoveLastWaypoint()
  {
    if (InventoryManager.ConstructionCursor is Frame)
    {
      return;
    }

    if (Waypoints.Count > 1)
    {
      Waypoints.RemoveAt(Waypoints.Count - 1);
    }
  }

  /// <summary>
  /// Places a single preview structure using the correct build index and spawn source.
  /// </summary>
  private static void PlaceStructure(InventoryManager inventoryManager, Structure item, int structureIndex)
  {
    var buildIndex = ResolveBuildIndex(inventoryManager, item, structureIndex);
    if (buildIndex < 0)
    {
      ZoopMod.Log($"Unable to resolve build index for {item.PrefabName}; skipping zoop placement.", ZoopMod.Logs.error);
      return;
    }

    inventoryManager.ConstructionPanel.BuildIndex = buildIndex;
    // Keep the authoring tool's original spawn source so UsePrimaryComplete can resolve the selected prefab family.
    InventoryManager.SpawnPrefab = InventoryManager.IsAuthoringMode && _zoopSpawnPrefab != null
      ? _zoopSpawnPrefab
      : item;
    UsePrimaryPositionField?.SetValue(inventoryManager, item.transform.position);
    UsePrimaryRotationField?.SetValue(inventoryManager, item.transform.rotation);
    if (UsePrimaryCompleteMethod == null)
    {
      ZoopMod.Log("Unable to find InventoryManager.UsePrimaryComplete; skipping zoop placement.", ZoopMod.Logs.error);
      return;
    }

    UsePrimaryCompleteMethod.Invoke(inventoryManager, null);

    if (!InventoryManager.IsAuthoringMode)
    {
      return;
    }

    var placedStructure = Structure.LastCreatedStructure;
    if (placedStructure?.NextBuildState == null)
    {
      return;
    }

    var lastBuildStateIndex = placedStructure.BuildStates.Count - 1;
    if (lastBuildStateIndex >= 0)
    {
      placedStructure.UpdateBuildStateAndVisualizer(lastBuildStateIndex);
    }
  }

  /// <summary>
  /// Resolves the build index that matches a preview structure.
  /// </summary>
  private static int ResolveBuildIndex(InventoryManager inventoryManager, Structure item, int structureIndex)
  {
    if (structureIndex >= 0 && structureIndex < StructureBuildIndices.Count)
    {
      return StructureBuildIndices[structureIndex];
    }

    var buildIndex =
      inventoryManager.ConstructionPanel.Parent.Constructables.FindIndex(structure =>
        structure.PrefabName == item.PrefabName);
    if (buildIndex >= 0)
    {
      return buildIndex;
    }

    return inventoryManager.ConstructionPanel.BuildIndex;
  }

  /// <summary>
  /// Returns the constructable at the given build index, or null when the index is invalid.
  /// </summary>
  private static Structure GetConstructableForBuildIndex(InventoryManager inventoryManager, int buildIndex)
  {
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    return buildIndex >= 0 && buildIndex < constructables.Count
      ? constructables[buildIndex]
      : null;
  }

  /// <summary>
  /// Adds the next straight or corner preview structure when enough resources are available.
  /// </summary>
  private static void AddStructure(List<Structure> constructables, bool isCorner, int index, int secondaryCount,
    ref bool canBuildNext, InventoryManager im, bool supportsCornerVariant)
  {
    var selectedIndex = im.ConstructionPanel.Parent.LastSelectedIndex;
    var straightCount = isCorner ? secondaryCount : index;
    var cornerCount = isCorner ? index : secondaryCount;

    var activeItem = constructables[selectedIndex];
    if (!isCorner && supportsCornerVariant)
    {
      switch (activeItem)
      {
        case Pipe or Cable or Frame when selectedIndex != 0:
        case Chute when selectedIndex != 0 && selectedIndex != 2:
          selectedIndex = 0;
          break;
      }
    }

    var activeHandItem = InventoryManager.ActiveHandSlot.Get();
    switch (activeHandItem)
    {
      case Stackable constructor:
        var canMakeItem = activeItem switch
        {
          Chute when selectedIndex == 0 => constructor.Quantity > Structures.Count,
          Chute when selectedIndex == 2 => constructor.Quantity > straightCount * 2 + (isCorner ? 0 : 1) + cornerCount,
          _ => constructor.Quantity > Structures.Count
        };

        if (canMakeItem && canBuildNext)
        {
          MakeItem(constructables, isCorner, index, !isCorner ? selectedIndex : 1, supportsCornerVariant);
          canBuildNext = true;
        }
        else
        {
          canBuildNext = false;
        }

        break;
      case AuthoringTool:
        MakeItem(constructables, isCorner, index, !isCorner ? selectedIndex : 1, supportsCornerVariant);
        canBuildNext = true;
        break;
    }
  }

  /// <summary>
  /// Destroys cached preview structures and resets the preview caches.
  /// </summary>
  private static void ClearStructureCache()
  {
    foreach (var structure in structuresCacheStraight)
    {
      structure.gameObject.SetActive(false);
      UnityObject.Destroy(structure);
    }

    structuresCacheStraight.Clear();
    StructureCacheStraightBuildIndices.Clear();

    foreach (var structure in structuresCacheCorner)
    {
      structure.gameObject.SetActive(false);
      UnityObject.Destroy(structure);
    }

    structuresCacheCorner.Clear();
    StructureCacheCornerBuildIndices.Clear();
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
  /// Reuses or creates a preview structure and adds it to the active zoop list.
  /// </summary>
  private static void MakeItem(List<Structure> constructables, bool isCorner, int index, int selectedIndex,
    bool supportsCornerVariant)
  {
    switch (isCorner)
    {
      case false when structuresCacheStraight.Count > index:
        {
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(structuresCacheStraight[index]);
          }

          Structures.Add(structuresCacheStraight[index]);
          StructureBuildIndices.Add(StructureCacheStraightBuildIndices[index]);
          break;
        }
      case true when structuresCacheCorner.Count > index:
        {
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(structuresCacheCorner[index]);
          }

          Structures.Add(structuresCacheCorner[index]);
          StructureBuildIndices.Add(StructureCacheCornerBuildIndices[index]);
          break;
        }
      default:
        {
          var structure = constructables[selectedIndex];
          if (structure == null)
          {
            return;
          }

          var structureNew = UnityObject.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
          if (structureNew != null)
          {
            structureNew.gameObject.SetActive(true);
            if (!supportsCornerVariant)
            {
              ApplyCursorRotation(structureNew);
            }

            Structures.Add(structureNew);
            StructureBuildIndices.Add(selectedIndex);
            if (isCorner)
            {
              structuresCacheCorner.Add(structureNew);
              StructureCacheCornerBuildIndices.Add(selectedIndex);
            }
            else
            {
              structuresCacheStraight.Add(structureNew);
              StructureCacheStraightBuildIndices.Add(selectedIndex);
            }
          }

          break;
        }
    }
  }

  /// <summary>
  /// Applies the current cursor rotation to a preview structure.
  /// </summary>
  private static void ApplyCursorRotation(Structure structure)
  {
    if (structure == null || InventoryManager.ConstructionCursor == null)
    {
      return;
    }

    var rotation = structure is Wall && _zoopStartWallNormal != Vector3.zero
      ? _zoopStartRotation
      : InventoryManager.ConstructionCursor.transform.rotation;

    SetStructureRotation(structure, rotation);
  }

  /// <summary>
  /// Locks wall zoops to the plane defined by the starting wall face.
  /// </summary>
  private static Vector3 ClampWallZoopPositionToStartPlane(Vector3 startPos, Vector3 targetPos)
  {
    if (InventoryManager.ConstructionCursor is not Wall || _zoopStartWallNormal == Vector3.zero)
    {
      return targetPos;
    }

    if (Mathf.Abs(_zoopStartWallNormal.x) > 0.99f)
    {
      targetPos.x = startPos.x;
    }
    else if (Mathf.Abs(_zoopStartWallNormal.y) > 0.99f)
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
    return Vector3.SqrMagnitude(first - second) < PositionToleranceSqr;
  }

  /// <summary>
  /// Compares two optional zoop positions, treating matching null states as equal.
  /// </summary>
  private static bool IsSameZoopPosition(Vector3? first, Vector3? second)
  {
    if (!first.HasValue || !second.HasValue)
    {
      return first.HasValue == second.HasValue;
    }

    return IsSameZoopPosition(first.Value, second.Value);
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
  /// Finds the waypoint index that matches the supplied position.
  /// </summary>
  private static int GetWaypointIndex(Vector3 position)
  {
    for (var index = 0; index < Waypoints.Count; index++)
    {
      if (Waypoints[index].HasValue && IsSameZoopPosition(Waypoints[index].Value, position))
      {
        return index;
      }
    }

    return -1;
  }

  /// <summary>
  /// Applies a rotation to both the structure state and Unity transform.
  /// </summary>
  private static void SetStructureRotation(Structure structure, Quaternion rotation)
  {
    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
  }

  /// <summary>
  /// Updates preview colors to reflect waypoint roles, errors, and network connections.
  /// </summary>
  private static void SetColor(InventoryManager inventoryManager, Structure structure, bool hasError)
  {
    var canConstruct = !hasError;
    var waypointIndex = GetWaypointIndex(structure.Position);
    var isWaypoint = waypointIndex >= 0;
    var isStart = waypointIndex == 0;
    Color color;
    if (!canConstruct)
    {
      color = ErrorColor;
    }
    else if (isStart)
    {
      color = StartColor;
    }
    else if (isWaypoint)
    {
      color = WaypointColor;
    }
    else
    {
      color = LineColor;
    }

    if (structure is SmallGrid smallGrid)
    {
      var list = smallGrid.WillJoinNetwork();
      foreach (var openEnd in smallGrid.OpenEnds)
      {
        if (canConstruct)
        {
          var colorToSet = list.Contains(openEnd)
            ? Color.yellow.SetAlpha(inventoryManager.CursorAlphaConstructionHelper)
            : Color.green.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
          foreach (var renderer in smallGrid.Renderers.Where(renderer => renderer.HasRenderer()))
          {
            renderer.SetColor(colorToSet);
          }

          foreach (var end in smallGrid.OpenEnds)
          {
            end.HelperRenderer.material.color = colorToSet;
          }
        }
        else
        {
          foreach (var renderer in smallGrid.Renderers.Where(renderer => renderer.HasRenderer()))
          {
            renderer.SetColor(Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper));
          }

          foreach (var end in smallGrid.OpenEnds)
          {
            end.HelperRenderer.material.color = Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
          }
        }
      }

      color = canConstruct && list.Count > 0 ? Color.yellow : color;
    }

    color.a = inventoryManager.CursorAlphaConstructionMesh;
    structure.Wireframe.BlueprintRenderer.material.color = color;
    //may it affect end structure lineColor at collided pieces and merge same colored cables?
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
  /// Returns whether authoring-mode previews should use the game's placement validator.
  /// </summary>
  private static bool ShouldUseAuthoringPlacementValidation()
  {
    return InventoryManager.IsAuthoringMode && InventoryManager.ActiveHandSlot.Get() is AuthoringTool;
  }

  /// <summary>
  /// Uses the game's placement validation when the preview clone has enough state for it.
  /// </summary>
  private static bool TryCanConstructAuthoringPlacement(Structure structure, out bool canConstruct)
  {
    try
    {
      var canConstructInfo = structure.CanConstruct();
      canConstruct = canConstructInfo.CanConstruct;
      return true;
    }
    catch (NullReferenceException)
    {
      canConstruct = false;
      return false;
    }
  }

  /// <summary>
  /// Checks whether a small-grid preview structure can be built in its current cell.
  /// </summary>
  private static bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return CanConstructWithValidationCursor(inventoryManager, structure, structureIndex,
      validationCursor => ValidateSmallCellCursor(inventoryManager, validationCursor));
  }

  /// <summary>
  /// Checks whether a large-grid preview structure can be built in its current cell.
  /// </summary>
  private static bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return CanConstructWithValidationCursor(inventoryManager, structure, structureIndex, ValidateLargeGridCursor);
  }

  /// <summary>
  /// Validates a zoop preview by temporarily retargeting the live construction cursor.
  /// </summary>
  private static bool CanConstructWithValidationCursor(InventoryManager inventoryManager, Structure structure,
    int structureIndex, Func<Structure, bool> validator)
  {
    var originalCursor = InventoryManager.ConstructionCursor;
    if (originalCursor == null)
    {
      return false;
    }

    // Capture the currently selected cursor before we temporarily retarget it to the zoop preview piece.
    if (!TryGetValidationTarget(inventoryManager, structure, structureIndex, originalCursor,
          out var validationConstructable, out var cursorState))
    {
      return false;
    }

    AllowPlacementUpdate = true;
    try
    {
      // Rebuild the live cursor so the game validates this preview as if the player were placing it directly.
      var validationCursor = PrepareValidationCursor(inventoryManager, structure, validationConstructable,
        cursorState);
      if (validationCursor == null)
      {
        return false;
      }

      return validator(validationCursor);
    }
    catch
    {
      return false;
    }
    finally
    {
      RestoreValidationCursor(inventoryManager, cursorState);
    }
  }

  /// <summary>
  /// Resolves the constructable and cursor snapshot needed for temporary small-grid validation.
  /// </summary>
  private static bool TryGetValidationTarget(InventoryManager inventoryManager, Structure structure,
    int structureIndex, Structure originalCursor, out Structure validationConstructable,
    out ValidationCursorState cursorState)
  {
    var originalBuildIndex = inventoryManager.ConstructionPanel.BuildIndex;
    var validationBuildIndex = ResolveBuildIndex(inventoryManager, structure, structureIndex);
    validationConstructable = GetConstructableForBuildIndex(inventoryManager, validationBuildIndex);
    if (validationConstructable == null)
    {
      cursorState = default;
      return false;
    }

    var needsCursorSwap = originalBuildIndex != validationBuildIndex ||
                          originalCursor.PrefabName != validationConstructable.PrefabName;
    cursorState = new ValidationCursorState(
      originalBuildIndex,
      validationBuildIndex,
      needsCursorSwap,
      originalCursor.ThingTransformPosition,
      originalCursor.ThingTransformLocalPosition,
      originalCursor.ThingTransformRotation,
      originalCursor.ThingTransformLocalRotation);
    return true;
  }

  /// <summary>
  /// Prepares the live construction cursor so the game can validate one zoop preview piece.
  /// </summary>
  private static Structure PrepareValidationCursor(InventoryManager inventoryManager, Structure structure,
    Structure validationConstructable, ValidationCursorState cursorState)
  {
    if (cursorState.NeedsCursorSwap)
    {
      inventoryManager.ConstructionPanel.BuildIndex = cursorState.ValidationBuildIndex;
      InventoryManager.UpdatePlacement(validationConstructable);
    }

    var validationCursor = InventoryManager.ConstructionCursor;
    if (validationCursor == null)
    {
      return null;
    }

    ApplyStructurePlacementState(validationCursor, structure.ThingTransformPosition, structure.ThingTransformLocalPosition,
      structure.ThingTransformRotation, structure.ThingTransformLocalRotation);
    validationCursor.CheckBounds();
    validationCursor.RebuildGridState();
    return validationCursor;
  }

  /// <summary>
  /// Applies the same native placement checks the game uses for the live preview cursor.
  /// </summary>
  private static bool ValidateSmallCellCursor(InventoryManager inventoryManager, Structure validationCursor)
  {
    var canConstruct = validationCursor.CanConstruct().CanConstruct;
    if (!canConstruct || InventoryManager.IsAuthoringMode || validationCursor is not IGridMergeable mergeable)
    {
      return canConstruct;
    }

    // Non-authoring previews do an extra merge/tool check for cables, pipes, and other grid-mergeables.
    var activeConstructor = InventoryManager.Parent.Slots[inventoryManager.ActiveHand.SlotId].Get() as MultiConstructor;
    var inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
    return mergeable.CanReplace(activeConstructor, inactiveHandOccupant).CanConstruct;
  }

  /// <summary>
  /// Checks whether a large-grid preview should be considered valid by the live cursor.
  /// </summary>
  private static bool ValidateLargeGridCursor(Structure validationCursor)
  {
    if (validationCursor is Wall)
    {
      var canMount = validationCursor.CanMountOnWall();
      if (!canMount)
      {
        return false;
      }
    }

    return validationCursor.CanConstruct().CanConstruct;
  }

  /// <summary>
  /// Restores the live construction cursor after a temporary small-grid validation check.
  /// </summary>
  private static void RestoreValidationCursor(InventoryManager inventoryManager,
    ValidationCursorState cursorState)
  {
    try
    {
      if (cursorState.NeedsCursorSwap)
      {
        inventoryManager.ConstructionPanel.BuildIndex = cursorState.OriginalBuildIndex;
        var originalConstructable = GetConstructableForBuildIndex(inventoryManager, cursorState.OriginalBuildIndex);
        if (originalConstructable != null)
        {
          InventoryManager.UpdatePlacement(originalConstructable);
        }
      }

      var restoredCursor = InventoryManager.ConstructionCursor;
      if (!cursorState.NeedsCursorSwap && restoredCursor != null)
      {
        ApplyStructurePlacementState(restoredCursor, cursorState.OriginalCursorPosition,
          cursorState.OriginalCursorLocalPosition, cursorState.OriginalCursorRotation,
          cursorState.OriginalCursorLocalRotation);
      }

      if (restoredCursor != null)
      {
        restoredCursor.gameObject.SetActive(false);
      }
    }
    finally
    {
      AllowPlacementUpdate = false;
    }
  }

  /// <summary>
  /// Applies world and local placement state to a structure.
  /// </summary>
  private static void ApplyStructurePlacementState(Structure structure, Vector3 position, Vector3 localPosition,
    Quaternion rotation, Quaternion localRotation)
  {
    structure.ThingTransformPosition = position;
    structure.Position = position;
    structure.transform.position = position;
    structure.ThingTransformLocalPosition = localPosition;
    structure.ThingTransformLocalRotation = localRotation;
    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
  }

  #endregion

  #region SmallGrid Methods

  /// <summary>
  /// Calculates the ordered axis segments needed to zoop between two small-grid positions.
  /// </summary>
  private static void CalculateZoopSegments(Vector3 startPos, Vector3 endPos, ZoopSegment segment)
  {
    segment.Directions.Clear();

    var startX = startPos.x;
    var startY = startPos.y;
    var startZ = startPos.z;
    var endX = endPos.x;
    var endY = endPos.y;
    var endZ = endPos.z;

    var absX = Math.Abs(endX - startX);
    var absY = Math.Abs(endY - startY);
    var absZ = Math.Abs(endZ - startZ);

    if (absX > float.Epsilon)
    {
      segment.CountX = 1 + (int)(Math.Abs(startX - endX) * 2);
      segment.IncreasingX = startX < endX;
      segment.Directions.Add(ZoopDirection.x);
    }

    if (absY > float.Epsilon)
    {
      segment.CountY = 1 + (int)(Math.Abs(startY - endY) * 2);
      segment.IncreasingY = startY < endY;
      segment.Directions.Add(ZoopDirection.y);
    }

    if (absZ > float.Epsilon)
    {
      segment.CountZ = 1 + (int)(Math.Abs(startZ - endZ) * 2);
      segment.IncreasingZ = startZ < endZ;
      segment.Directions.Add(ZoopDirection.z);
    }
  }

  /// <summary>
  /// Builds the preview structure list for a small-grid zoop path.
  /// </summary>
  private static void BuildSmallStructureList(InventoryManager inventoryManager, List<ZoopSegment> zoops,
    bool supportsCornerVariant)
  {
    Structures.Clear();
    StructureBuildIndices.Clear();
    structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
    structuresCacheCorner.ForEach(structure => structure.GameObject.SetActive(false));

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
        var zoopCounter = GetCountForDirection(zoopDirection, segment);

        zoopCounter = GetPlacementCount(zoops.Count, segmentIndex, segment.Directions.Count, directionIndex,
          zoopCounter);

        for (var j = 0; j < zoopCounter; j++)
        {
          if (Structures.Count > 0 && (j == 0 || segmentIndex > 0) && supportsCornerVariant)
          {
            if (zoopDirection != lastDirection)
            {
              AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, true, corners, straight,
                ref canBuildNext, inventoryManager,
                supportsCornerVariant); // start with corner on secondary and tertiary zoop directions
              corners++;
            }
            else
            {
              AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, corners,
                ref canBuildNext, inventoryManager, supportsCornerVariant);
              straight++;
            }
          }
          else
          {
            AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, corners,
              ref canBuildNext, inventoryManager, supportsCornerVariant);
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

  /// <summary>
  /// Returns the placement count for a given zoop segment direction.
  /// </summary>
  private static int GetCountForDirection(ZoopDirection direction, ZoopSegment segment)
  {
    return direction switch
    {
      ZoopDirection.x => segment.CountX,
      ZoopDirection.y => segment.CountY,
      ZoopDirection.z => segment.CountZ,
      _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };
  }

  /// <summary>
  /// Returns whether placements along the given zoop segment direction increase positively.
  /// </summary>
  private static bool GetIncreasingForDirection(ZoopDirection direction, ZoopSegment segment)
  {
    return direction switch
    {
      ZoopDirection.x => segment.IncreasingX,
      ZoopDirection.y => segment.IncreasingY,
      ZoopDirection.z => segment.IncreasingZ,
      _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };
  }

  #endregion

  #region BigGrid Methods

  /// <summary>
  /// Calculates the two-axis plane used for a large-grid zoop.
  /// </summary>
  private static void CalculateZoopPlane(Vector3 startPos, Vector3 endPos, ZoopPlane plane)
  {
    var startX = startPos.x;
    var startY = startPos.y;
    var startZ = startPos.z;
    var endX = endPos.x;
    var endY = endPos.y;
    var endZ = endPos.z;

    var absX = Math.Abs(endX - startX) / 2;
    var absY = Math.Abs(endY - startY) / 2;
    var absZ = Math.Abs(endZ - startZ) / 2;

    var directions = new List<(float value, ZoopDirection direction, int count, bool increasing)>
    {
      (absX, ZoopDirection.x, 1 + (int)(Math.Abs(startX - endX) / 2), startX < endX),
      (absY, ZoopDirection.y, 1 + (int)(Math.Abs(startY - endY) / 2), startY < endY),
      (absZ, ZoopDirection.z, 1 + (int)(Math.Abs(startZ - endZ) / 2), startZ < endZ)
    };

    directions.Sort((a, b) => b.value.CompareTo(a.value));

    plane.Directions = (direction1: directions[0].direction, direction2: directions[1].direction);
    plane.Count = (direction1: directions[0].count, direction2: directions[1].count);
    plane.Increasing = (direction1: directions[0].increasing, direction2: directions[1].increasing);
  }

  /// <summary>
  /// Builds the preview structure list for a large-grid zoop plane.
  /// </summary>
  private static void BuildBigStructureList(InventoryManager inventoryManager, ZoopPlane plane)
  {
    Structures.Clear();
    StructureBuildIndices.Clear();
    structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    {
      for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
      {
        AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0, ref canBuildNext,
          inventoryManager, false);
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

  /// <summary>
  /// Adjusts the number of placements so shared corners are not duplicated.
  /// </summary>
  private static int GetPlacementCount(int segmentCount, int segmentIndex, int directionCount, int directionIndex,
    int zoopCount)
  {
    if ((segmentIndex < segmentCount - 1 && directionIndex == directionCount - 1) ||
        directionIndex < directionCount - 1)
    {
      return zoopCount - 1;
    }

    return zoopCount;
  }

  /// <summary>
  /// Applies an offset value to the axis that matches the supplied zoop direction.
  /// </summary>
  private static void SetDirectionalOffset(ref float xOffset, ref float yOffset, ref float zOffset,
    ZoopDirection direction, float value)
  {
    switch (direction)
    {
      case ZoopDirection.x:
        xOffset = value;
        return;
      case ZoopDirection.y:
        yOffset = value;
        return;
      case ZoopDirection.z:
        zOffset = value;
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
    }
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
