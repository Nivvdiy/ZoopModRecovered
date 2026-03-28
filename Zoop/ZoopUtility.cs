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

  private static readonly FieldInfo UsePrimaryPositionField =
    typeof(InventoryManager).GetField("_usePrimaryPosition", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly FieldInfo UsePrimaryRotationField =
    typeof(InventoryManager).GetField("_usePrimaryRotation", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly MethodInfo UsePrimaryCompleteMethod = typeof(InventoryManager).GetMethod("UsePrimaryComplete",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

  public static bool IsZoopKeyPressed { get; set; }

  public static bool isZooping { get; private set; }
  private static int spacing = 1;

  public static Color LineColor { get; set; } = Color.green;
  private static Color ErrorColor { get; } = Color.red;
  private static readonly Color WaypointColor = Color.blue;
  private static readonly Color StartColor = Color.magenta;

  #endregion

  #region Common Methods

  public static void StartZoop(InventoryManager inventoryManager)
  {
    if (!IsAllowed(InventoryManager.ConstructionCursor)) return;
    isZooping = true;
    if (_zoopCancellationSource == null)
    {
      Waypoints.Clear();
      _zoopSpawnPrefab = InventoryManager.SpawnPrefab;
      if (InventoryManager.ConstructionCursor != null)
      {
        var selectedConstructable = GetSelectedConstructable(inventoryManager);
        AllowPlacementUpdate = true;
        try
        {
          if (selectedConstructable != null) InventoryManager.UpdatePlacement(selectedConstructable);
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
        if (startPos.HasValue) Waypoints.Add(startPos); // Add start position as the first waypoint
      }

      if (Waypoints.Count > 0)
      {
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
          }
        }, cancellationToken: ct);
      }
    }
    else
    {
      CancelZoop();
    }
  }

  public static void CancelZoop()
  {
    isZooping = false;
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
      InventoryManager.ConstructionCursor.gameObject.SetActive(true);
  }

  public static void SetPendingBuild(InventoryManager inventoryManager, Coroutine coroutine)
  {
    CancelPendingBuild();
    ActionCoroutine = coroutine;
    _actionCoroutineOwner = inventoryManager;
  }

  private static void CancelPendingBuild()
  {
    if (_actionCoroutineOwner != null && ActionCoroutine != null) _actionCoroutineOwner.StopCoroutine(ActionCoroutine);

    ClearPendingBuild();
  }

  private static void ClearPendingBuild()
  {
    ActionCoroutine = null;
    _actionCoroutineOwner = null;
  }

  private static async UniTask ZoopAsync(CancellationToken cancellationToken, InventoryManager inventoryManager)
  {
    await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

    List<ZoopSegment> zoops = [];
    if (InventoryManager.ConstructionCursor != null)
      InventoryManager.ConstructionCursor.gameObject.SetActive(false);

    while (cancellationToken is { IsCancellationRequested: false })
      try
      {
        if (Waypoints.Count > 0)
        {
          var currentPos = GetCurrentMouseGridPosition();
          if (currentPos.HasValue)
          {
            zoops.Clear();
            HasError = false;
            var singleItem = true;

            if (IsZoopingSmallGrid())
            {
              // Iterate over each pair of waypoints
              for (var wpIndex = 0; wpIndex < Waypoints.Count; wpIndex++)
              {
                var startPos = Waypoints[wpIndex].Value;
                var endPos = wpIndex < Waypoints.Count - 1 ? Waypoints[wpIndex + 1].Value : currentPos.Value;

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

              await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

              var supportsCornerVariant =
                SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables,
                  inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
              BuildSmallStructureList(inventoryManager, zoops, supportsCornerVariant);

              var structureCounter = 0;

              if (Structures.Count > 0)
              {
                var lastDirection = ZoopDirection.none;
                for (var segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++)
                {
                  var segment = zoops[segmentIndex];
                  float xOffset = 0;
                  float yOffset = 0;
                  float zOffset = 0;
                  var startPos = Waypoints[segmentIndex].Value;
                  for (var directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
                  {
                    if (structureCounter == Structures.Count) break;

                    var zoopDirection = segment.Directions[directionIndex];
                    var increasing = GetIncreasingForDirection(zoopDirection, segment);
                    var zoopCounter = GetCountForDirection(zoopDirection, segment);

                    zoopCounter = GetPlacementCount(zoops.Count, segmentIndex, segment.Directions.Count, directionIndex,
                      zoopCounter);

                    for (var zi = 0; zi < zoopCounter; zi++)
                    {
                      if (structureCounter == Structures.Count) break;

                      spacing = Mathf.Max(spacing, 1);
                      var minValue = InventoryManager.ConstructionCursor is SmallGrid ? 0.5f : 2f;
                      var value = increasing ? minValue * spacing : -(minValue * spacing);
                      SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, zi * value);

                      var increasingTo = increasing;
                      var increasingFrom = lastDirection != ZoopDirection.none &&
                                           GetIncreasingForDirection(lastDirection, segment);
                      // Correct the logic to avoid overlapping
                      if (segmentIndex > 0 && directionIndex == 0 && zi == 0)
                      {
                        var lastSegmentDirection = zoops[segmentIndex - 1].Directions.Last();
                        switch (lastSegmentDirection)
                        {
                          case ZoopDirection.x:
                            increasingFrom = zoops[segmentIndex - 1].IncreasingX;
                            break;
                          case ZoopDirection.y:
                            increasingFrom = zoops[segmentIndex - 1].IncreasingY;
                            break;
                          case ZoopDirection.z:
                            increasingFrom = zoops[segmentIndex - 1].IncreasingZ;
                            break;
                          case ZoopDirection.none:
                          default:
                            throw new ArgumentOutOfRangeException();
                        }
                      }

                      if (supportsCornerVariant)
                      {
                        if ((directionIndex > 0 || (segmentIndex > 0 && directionIndex == 0)) && zi == 0)
                        {
                          if (lastDirection == zoopDirection)
                            SetStraightRotationSmallGrid(Structures[structureCounter], zoopDirection);
                          else
                            SetCornerRotation(Structures[structureCounter], lastDirection, increasingFrom,
                              zoopDirection, increasingTo);
                        }
                        else if (!singleItem)
                        {
                          SetStraightRotationSmallGrid(Structures[structureCounter], zoopDirection);
                        }
                      }

                      lastDirection = zoopDirection;

                      var offset = new Vector3(xOffset, yOffset, zOffset);
                      Structures[structureCounter].GameObject.SetActive(true);
                      Structures[structureCounter].ThingTransformPosition = startPos + offset;
                      Structures[structureCounter].Position = startPos + offset;
                      if (!ZoopMod.CFree)
                        HasError = HasError || !CanConstructSmallCell(inventoryManager, Structures[structureCounter]);
                      structureCounter++;
                      if (zi == zoopCounter - 1)
                        SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection, (zi + 1) * value);
                    }
                  }
                }
              }
            }
            else if (IsZoopingBigGrid())
            {
              var startPos = Waypoints[0].Value;
              var endPos = ClampWallZoopPositionToStartPlane(startPos, currentPos.Value);

              var plane = new ZoopPlane();
              CalculateZoopPlane(startPos, endPos, plane);

              singleItem = IsSameZoopPosition(startPos, endPos);
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

              if (Structures.Count > 0)
              {
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
                    if (structureCounter == Structures.Count) break;

                    var zoopDirection1 = plane.Directions.direction1;
                    var increasing1 = plane.Increasing.direction1;

                    var value1 = increasing1 ? 2f * spacing : -(2f * spacing);
                    SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, zoopDirection1,
                      indexDirection1 * value1);

                    var offset = new Vector3(xOffset, yOffset, zOffset);
                    var previewPosition = startPos + offset;
                    Structures[structureCounter].GameObject.SetActive(true);
                    Structures[structureCounter].ThingTransformPosition = previewPosition;
                    Structures[structureCounter].Position = previewPosition;
                    HasError = HasError || !CanConstructBigCell(Structures[structureCounter]);
                    structureCounter++;
                  }
                }
              }
            }

            foreach (var structure in Structures) SetColor(inventoryManager, structure, HasError);
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

  public static void AddWaypoint()
  {
    if (InventoryManager.ConstructionCursor is Frame) return;
    var currentPos = GetCurrentMouseGridPosition();
    if (currentPos.HasValue && !IsSameZoopPosition(Waypoints.Last(), currentPos))
    {
      if (Structures.Count > 0 && IsSameZoopPosition(Structures.Last().Position, currentPos.Value))
        Waypoints.Add(currentPos);
    }
    else if (IsSameZoopPosition(Waypoints.Last(), currentPos))
    {
      //TODO show message to user that waypoint is already added
    }
  }

  public static void RemoveLastWaypoint()
  {
    if (InventoryManager.ConstructionCursor is Frame) return;
    if (Waypoints.Count > 1) Waypoints.RemoveAt(Waypoints.Count - 1);
  }

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

    if (!InventoryManager.IsAuthoringMode) return;

    var placedStructure = Structure.LastCreatedStructure;
    if (placedStructure?.NextBuildState == null) return;

    var lastBuildStateIndex = placedStructure.BuildStates.Count - 1;
    if (lastBuildStateIndex >= 0) placedStructure.UpdateBuildStateAndVisualizer(lastBuildStateIndex);
  }

  private static int ResolveBuildIndex(InventoryManager inventoryManager, Structure item, int structureIndex)
  {
    if (structureIndex >= 0 && structureIndex < StructureBuildIndices.Count)
      return StructureBuildIndices[structureIndex];

    var buildIndex =
      inventoryManager.ConstructionPanel.Parent.Constructables.FindIndex(structure =>
        structure.PrefabName == item.PrefabName);
    if (buildIndex >= 0) return buildIndex;

    return inventoryManager.ConstructionPanel.BuildIndex;
  }

  private static void AddStructure(List<Structure> constructables, bool isCorner, int index, int secondaryCount,
    ref bool canBuildNext, InventoryManager im, bool supportsCornerVariant)
  {
    var selectedIndex = im.ConstructionPanel.Parent.LastSelectedIndex;
    var straightCount = isCorner ? secondaryCount : index;
    var cornerCount = isCorner ? index : secondaryCount;

    var activeItem = constructables[selectedIndex];
    if (!isCorner && supportsCornerVariant)
      switch (activeItem)
      {
        case Pipe or Cable or Frame when selectedIndex != 0:
        case Chute when selectedIndex != 0 && selectedIndex != 2:
          selectedIndex = 0;
          break;
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

  private static Vector3? GetCurrentMouseGridPosition()
  {
    if (InventoryManager.ConstructionCursor == null) return null;

    if (InventoryManager.ConstructionCursor is Wall) return InventoryManager.ConstructionCursor.ThingTransformPosition;

    var cursorHitPoint = InventoryManager.ConstructionCursor.GetLocalGrid().ToVector3();
    return cursorHitPoint;
  }

  private static void MakeItem(List<Structure> constructables, bool isCorner, int index, int selectedIndex,
    bool supportsCornerVariant)
  {
    switch (isCorner)
    {
      case false when structuresCacheStraight.Count > index:
        {
          if (!supportsCornerVariant) ApplyCursorRotation(structuresCacheStraight[index]);
          Structures.Add(structuresCacheStraight[index]);
          StructureBuildIndices.Add(StructureCacheStraightBuildIndices[index]);
          break;
        }
      case true when structuresCacheCorner.Count > index:
        {
          if (!supportsCornerVariant) ApplyCursorRotation(structuresCacheCorner[index]);
          Structures.Add(structuresCacheCorner[index]);
          StructureBuildIndices.Add(StructureCacheCornerBuildIndices[index]);
          break;
        }
      default:
        {
          var structure = constructables[selectedIndex];
          if (structure == null) return;

          var structureNew = UnityObject.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
          if (structureNew != null)
          {
            structureNew.gameObject.SetActive(true);
            if (!supportsCornerVariant) ApplyCursorRotation(structureNew);
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

  private static void ApplyCursorRotation(Structure structure)
  {
    if (structure == null || InventoryManager.ConstructionCursor == null) return;

    var rotation = structure is Wall && _zoopStartWallNormal != Vector3.zero
      ? _zoopStartRotation
      : InventoryManager.ConstructionCursor.transform.rotation;

    SetStructureRotation(structure, rotation);
  }

  private static Vector3 ClampWallZoopPositionToStartPlane(Vector3 startPos, Vector3 targetPos)
  {
    if (InventoryManager.ConstructionCursor is not Wall || _zoopStartWallNormal == Vector3.zero) return targetPos;

    if (Mathf.Abs(_zoopStartWallNormal.x) > 0.99f)
      targetPos.x = startPos.x;
    else if (Mathf.Abs(_zoopStartWallNormal.y) > 0.99f)
      targetPos.y = startPos.y;
    else
      targetPos.z = startPos.z;

    return targetPos;
  }

  private static bool IsSameZoopPosition(Vector3 first, Vector3 second)
  {
    // Mounted wall previews use world positions, so tiny float drift can appear between cursor, waypoint, and preview values.
    return Vector3.SqrMagnitude(first - second) < PositionToleranceSqr;
  }

  private static bool IsSameZoopPosition(Vector3? first, Vector3? second)
  {
    if (!first.HasValue || !second.HasValue) return first.HasValue == second.HasValue;

    return IsSameZoopPosition(first.Value, second.Value);
  }

  private static int GetWaypointIndex(Vector3 position)
  {
    for (var index = 0; index < Waypoints.Count; index++)
      if (Waypoints[index].HasValue && IsSameZoopPosition(Waypoints[index].Value, position))
        return index;

    return -1;
  }

  private static void SetStructureRotation(Structure structure, Quaternion rotation)
  {
    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
  }

  private static void SetColor(InventoryManager inventoryManager, Structure structure, bool hasError)
  {
    var canConstruct = !hasError;
    var waypointIndex = GetWaypointIndex(structure.Position);
    var isWaypoint = waypointIndex >= 0;
    var isStart = waypointIndex == 0;
    Color color;
    if (isWaypoint)
    {
      if (isStart)
        color = canConstruct ? StartColor : ErrorColor;
      else
        color = canConstruct ? WaypointColor : ErrorColor;
    }
    else
    {
      color = canConstruct ? LineColor : ErrorColor;
    }

    if (structure is SmallGrid smallGrid)
    {
      var list = smallGrid.WillJoinNetwork();
      foreach (var openEnd in smallGrid.OpenEnds)
        if (canConstruct)
        {
          var colorToSet = list.Contains(openEnd)
            ? Color.yellow.SetAlpha(inventoryManager.CursorAlphaConstructionHelper)
            : Color.green.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
          foreach (var renderer in smallGrid.Renderers.Where(renderer => renderer.HasRenderer()))
            renderer.SetColor(colorToSet);

          foreach (var end in smallGrid.OpenEnds) end.HelperRenderer.material.color = colorToSet;
        }
        else
        {
          foreach (var renderer in smallGrid.Renderers.Where(renderer => renderer.HasRenderer()))
            renderer.SetColor(Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper));

          foreach (var end in smallGrid.OpenEnds)
            end.HelperRenderer.material.color = Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
        }

      color = canConstruct && list.Count > 0 ? Color.yellow : color;
    }

    color.a = inventoryManager.CursorAlphaConstructionMesh;
    structure.Wireframe.BlueprintRenderer.material.color = color;
    //may it affect end structure lineColor at collided pieces and merge same colored cables?
  }

  #endregion

  #region Conditionnal Methods

  private static Structure GetSelectedConstructable(InventoryManager inventoryManager)
  {
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    var selectedIndex = inventoryManager.ConstructionPanel.Parent.LastSelectedIndex;
    if (selectedIndex >= 0 && selectedIndex < constructables.Count) return constructables[selectedIndex];

    return InventoryManager.ConstructionCursor;
  }

  private static bool IsAllowed(Structure constructionCursor)
  {
    return constructionCursor is Pipe or Cable or Chute or Frame or Wall;
  }

  private static bool IsZoopingSmallGrid()
  {
    return InventoryManager.ConstructionCursor is SmallGrid;
  }

  private static bool IsZoopingBigGrid()
  {
    return InventoryManager.ConstructionCursor is LargeStructure;
  }

  private static bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure)
  {
    var smallCell = structure.GridController.GetSmallCell(structure.ThingTransformLocalPosition);
    var invalidStructureExistsOnGrid =
      smallCell != null &&
      ((smallCell.Device != null &&
        !((structure is Piping pipe && pipe == pipe.IsStraight && smallCell.Device is DevicePipeMounted device &&
           device.contentType == pipe.PipeContentType) ||
          (structure is Cable cable && cable == cable.IsStraight && smallCell.Device is DeviceCableMounted))) ||
       smallCell.Other != null);

    var differentEndsCollision = false;
    Type structureType = null;
    switch (structure)
    {
      case Piping:
        structureType = typeof(Piping);
        break;
      case Cable:
        structureType = typeof(Cable);
        break;
      case Chute:
        structureType = typeof(Chute);
        break;
    }

    if (structureType != null)
    {
      var method = structureType.GetMethod("_IsCollision", BindingFlags.Instance | BindingFlags.NonPublic);

      if (method != null)
      {
        differentEndsCollision = smallCell != null && smallCell.Cable != null &&
                                 (bool)method.Invoke(structure, [smallCell.Cable]);
        differentEndsCollision |= smallCell != null && smallCell.Pipe != null &&
                                  (bool)method.Invoke(structure, [smallCell.Pipe]);
        differentEndsCollision |= smallCell != null && smallCell.Chute != null &&
                                  (bool)method.Invoke(structure, [smallCell.Chute]);
      }
    }

    var canConstruct = !invalidStructureExistsOnGrid && !differentEndsCollision;

    if (smallCell != null && smallCell.IsValid() && structure is Piping && smallCell.Pipe is Piping piping)
    {
      var inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
      var canReplace = piping.CanReplace(inventoryManager.ConstructionPanel.Parent, inactiveHandOccupant);
      canConstruct &= canReplace.CanConstruct;
    }
    else if (smallCell != null && smallCell.IsValid() && structure is Cable && smallCell.Cable is { } cable2)
    {
      var inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
      var canReplace = cable2.CanReplace(inventoryManager.ConstructionPanel.Parent, inactiveHandOccupant);
      canConstruct &= canReplace.CanConstruct;
    }
    else if (smallCell != null && smallCell.IsValid() && structure is Chute && smallCell.Chute is not null)
    {
      canConstruct &= false;
    }

    return canConstruct;
  }

  private static bool CanConstructBigCell(Structure structure)
  {
    var cell = structure.GridController.GetCell(structure.ThingTransformLocalPosition);
    if (cell != null)
    {
      if (structure is Wall wall)
      {
        foreach (var cellStructure in cell.AllStructures)
        {
          if (cellStructure is Wall existingWall)
          {
            var samePosition = IsSameZoopPosition(existingWall.ThingTransformPosition, wall.ThingTransformPosition);
            var sameFace = Vector3.Dot(existingWall.transform.forward, wall.transform.forward) > 0.99f;
            if (samePosition && sameFace) return false;

            continue;
          }

          if (cellStructure is LargeStructure) return false;
        }

        return true;
      }

      return !cell.AllStructures.Any(static cellStructure => cellStructure is LargeStructure);
    }

    return true;
  }

  #endregion

  #region SmallGrid Methods

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

  private static bool SupportsCornerVariant(List<Structure> constructables, int selectedIndex)
  {
    if (constructables == null || selectedIndex < 0 || selectedIndex >= constructables.Count) return false;

    var selectedStructure = constructables[selectedIndex];
    if (selectedStructure == null) return false;

    return constructables.Any(structure =>
      IsCornerVariant(structure)
      && IsMatchingCornerFamily(selectedStructure, structure));
  }

  private static bool IsCornerVariant(Structure structure)
  {
    if (structure == null) return false;

    var prefabName = structure.GetPrefabName();
    if (!string.IsNullOrEmpty(prefabName) &&
        prefabName.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    return structure.GetType().Name.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0;
  }

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

  private static void BuildBigStructureList(InventoryManager inventoryManager, ZoopPlane plane)
  {
    Structures.Clear();
    StructureBuildIndices.Clear();
    structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
    var count = 0;
    var canBuildNext = true;

    for (var indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
    for (var indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
    {
      AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0, ref canBuildNext,
        inventoryManager, false);
      count++;
    }
  }

  private static Vector3 GetCardinalAxis(Vector3 vector)
  {
    var normalized = vector.normalized;
    var xAbs = Mathf.Abs(normalized.x);
    var yAbs = Mathf.Abs(normalized.y);
    var zAbs = Mathf.Abs(normalized.z);

    if (xAbs >= yAbs && xAbs >= zAbs) return normalized.x >= 0f ? Vector3.right : Vector3.left;

    if (yAbs >= zAbs) return normalized.y >= 0f ? Vector3.up : Vector3.down;

    return normalized.z >= 0f ? Vector3.forward : Vector3.back;
  }

  private static int GetPlacementCount(int segmentCount, int segmentIndex, int directionCount, int directionIndex,
    int zoopCount)
  {
    if ((segmentIndex < segmentCount - 1 && directionIndex == directionCount - 1) ||
        directionIndex < directionCount - 1)
      return zoopCount - 1;

    return zoopCount;
  }

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

  private static void SetCornerRotation(Structure structure, ZoopDirection zoopDirectionFrom, bool increasingFrom,
    ZoopDirection zoopDirectionTo, bool increasingTo)
  {
    var xOffset = 0.0f;
    var yOffset = 0.0f;
    var zOffset = 0.0f;
    if (structure.GetPrefabName().Equals("StructureCableCorner")) xOffset = 180.0f;

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
