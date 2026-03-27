using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using Objects.Structures;
using UnityEngine;
using Object = UnityEngine.Object; //SOMETHING NEW

//TODO make it work in authoring mode

namespace ZoopMod.Zoop
{

  public class ZoopUtility
  {

    #region Fields

    public static List<Structure> structures = [];
    private static readonly List<int> StructureBuildIndices = [];
    private static List<Structure> structuresCacheStraight = [];
    private static readonly List<int> StructureCacheStraightBuildIndices = [];
    private static List<Structure> structuresCacheCorner = [];
    private static readonly List<int> StructureCacheCornerBuildIndices = [];

    private static readonly List<Vector3?> Waypoints = [];

    //preferred zoop order is built up by every first detection of a direction
    private static readonly List<ZoopDirection> PreferredZoopOrder = [];

    public static bool HasError { get; private set; }
    public static Coroutine ActionCoroutine { get; set; }
    public static bool AllowPlacementUpdate { get; private set; }
    private static CancellationTokenSource _cancellationToken;
    private static Quaternion _zoopStartRotation = Quaternion.identity;
    private static Vector3 _zoopStartWallNormal = Vector3.zero;
    private static Vector3 _zoopStartWallPositionOffset = Vector3.zero;
    private static readonly FieldInfo UsePrimaryPositionField = typeof(InventoryManager).GetField("_usePrimaryPosition", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo UsePrimaryRotationField = typeof(InventoryManager).GetField("_usePrimaryRotation", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo UsePrimaryCompleteMethod = typeof(InventoryManager).GetMethod("UsePrimaryComplete", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static bool isZoopKeyPressed;
    public static bool isZooping { get; private set; }
    private static int spacing = 1;

    public static Color lineColor = Color.green;
    private static Color errorColor = Color.red;
    private static readonly Color WaypointColor = Color.blue;
    private static readonly Color StartColor = Color.magenta;

    #endregion

    #region Common Methods

    public static void StartZoop(InventoryManager inventoryManager)
    {
      if (IsAllowed(InventoryManager.ConstructionCursor))
      {
        isZooping = true;
        if (_cancellationToken == null)
        {
          PreferredZoopOrder.Clear();
          Waypoints.Clear();
          if (InventoryManager.ConstructionCursor != null)
          {
            Structure selectedConstructable = GetSelectedConstructable(inventoryManager);
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

            Vector3? startPos = GetCurrentMouseGridPosition();
            if (startPos.HasValue)
            {
              _zoopStartWallPositionOffset = InventoryManager.ConstructionCursor is Wall
                  ? InventoryManager.ConstructionCursor.ThingTransformPosition - startPos.Value
                  : Vector3.zero;
              Waypoints.Add(startPos); // Add start position as the first waypoint
            }
          }

          if (Waypoints.Count > 0)
          {
            _cancellationToken = new CancellationTokenSource();
            UniTask.RunOnThreadPool(async () => await ZoopAsync(_cancellationToken.Token, inventoryManager));
          }
        }
        else
        {
          CancelZoop();
        }
      }

    }

    public static void CancelZoop()
    {
      // NotAuthoringMode.Completion = false;
      isZooping = false;
      if (_cancellationToken != null)
      {
        _cancellationToken.Cancel();
        _cancellationToken = null;
        ClearStructureCache();
        structures.Clear(); //try to reset a list of structures for single piece placing
        StructureBuildIndices.Clear();
        Waypoints.Clear();
        _zoopStartRotation = Quaternion.identity;
        _zoopStartWallNormal = Vector3.zero;
        _zoopStartWallPositionOffset = Vector3.zero;
      }

      if (InventoryManager.ConstructionCursor != null)
        InventoryManager.ConstructionCursor.gameObject.SetActive(true);
    }

    private static async UniTask ZoopAsync(CancellationToken cancellationToken, InventoryManager inventoryManager)
    {

      await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

      List<ZoopSegment> zoops = [];
      if (InventoryManager.ConstructionCursor != null)
        InventoryManager.ConstructionCursor.gameObject.SetActive(false);

      while (cancellationToken is { IsCancellationRequested: false })
      {

        try
        {
          if (Waypoints.Count > 0)
          {
            Vector3? currentPos = GetCurrentMouseGridPosition();
            if (currentPos.HasValue)
            {
              zoops.Clear();
              HasError = false;
              bool singleItem = true;

              if (IsZoopingSmallGrid())
              {

                // Iterate over each pair of waypoints
                for (int wpIndex = 0; wpIndex < Waypoints.Count; wpIndex++)
                {
                  Vector3 startPos = Waypoints[wpIndex].Value;
                  Vector3 endPos = wpIndex < Waypoints.Count - 1 ? Waypoints[wpIndex + 1].Value : currentPos.Value;

                  ZoopSegment segment = new ZoopSegment();
                  CalculateZoopSegments(startPos, endPos, segment);

                  singleItem = startPos == endPos;
                  if (singleItem)
                  {
                    segment.CountX = 1 + (int)(Math.Abs(startPos.x - endPos.x) * 2);
                    segment.IncreasingX = startPos.x < endPos.x;
                    segment.Directions.Add(ZoopDirection.x); // unused for single item
                  }

                  zoops.Add(segment);
                }

                await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

                bool supportsCornerVariant = SupportsCornerVariant(inventoryManager.ConstructionPanel.Parent.Constructables, inventoryManager.ConstructionPanel.Parent.LastSelectedIndex);
                BuildSmallStructureList(inventoryManager, zoops, supportsCornerVariant);

                int structureCounter = 0;

                if (structures.Count > 0)
                {
                  ZoopDirection lastDirection = ZoopDirection.none;
                  for (int segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++)
                  {
                    ZoopSegment segment = zoops[segmentIndex];
                    float xOffset = 0;
                    float yOffset = 0;
                    float zOffset = 0;
                    Vector3 startPos = Waypoints[segmentIndex].Value;
                    for (int directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
                    {
                      if (structureCounter == structures.Count)
                      {
                        break;
                      }

                      ZoopDirection zoopDirection = segment.Directions[directionIndex];
                      bool increasing = GetIncreasingForDirection(zoopDirection, segment);
                      int zoopCounter = GetCountForDirection(zoopDirection, segment);

                      if (segmentIndex < zoops.Count - 1 && directionIndex == segment.Directions.Count - 1)
                      {
                        zoopCounter--;
                      }
                      else if (directionIndex < segment.Directions.Count - 1)
                      {
                        zoopCounter--;
                      }

                      for (int zi = 0; zi < zoopCounter; zi++)
                      {
                        if (structureCounter == structures.Count)
                        {
                          break;
                        }

                        spacing = Mathf.Max(spacing, 1);
                        float minValue = InventoryManager.ConstructionCursor is SmallGrid ? 0.5f : 2f;
                        float value = increasing ? minValue * spacing : -(minValue * spacing);
                        switch (zoopDirection)
                        {
                          case ZoopDirection.x:
                            xOffset = zi * value;
                            break;
                          case ZoopDirection.y:
                            yOffset = zi * value;
                            break;
                          case ZoopDirection.z:
                            zOffset = zi * value;
                            break;
                        }

                        bool increasingTo = increasing;
                        bool increasingFrom = lastDirection != ZoopDirection.none && GetIncreasingForDirection(lastDirection, segment);
                        // Correct the logic to avoid overlapping
                        if (segmentIndex > 0 && directionIndex == 0 && zi == 0)
                        {
                          ZoopDirection lastSegmentDirection = zoops[segmentIndex - 1].Directions.Last();
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
                          if ((directionIndex > 0 || segmentIndex > 0 && directionIndex == 0) && zi == 0)
                          {
                            if (lastDirection == zoopDirection)
                            {
                              SetStraightRotationSmallGrid(structures[structureCounter], zoopDirection);
                            }
                            else
                            {
                              SetCornerRotation(structures[structureCounter], lastDirection, increasingFrom, zoopDirection, increasingTo);
                            }
                          }
                          else if (!singleItem)
                          {
                            SetStraightRotationSmallGrid(structures[structureCounter], zoopDirection);
                          }
                        }
                        lastDirection = zoopDirection;

                        Vector3 offset = new Vector3(xOffset, yOffset, zOffset);
                        structures[structureCounter].GameObject.SetActive(true);
                        structures[structureCounter].ThingTransformPosition = startPos + offset;
                        structures[structureCounter].Position = startPos + offset;
                        if (!ZoopMod.CFree)
                        {
                          HasError = HasError || !CanConstructSmallCell(inventoryManager, structures[structureCounter]);
                        }
                        structureCounter++;
                        if (zi == zoopCounter - 1)
                        {
                          switch (zoopDirection)
                          {
                            case ZoopDirection.x:
                              xOffset = (zi + 1) * value;
                              break;
                            case ZoopDirection.y:
                              yOffset = (zi + 1) * value;
                              break;
                            case ZoopDirection.z:
                              zOffset = (zi + 1) * value;
                              break;
                            case ZoopDirection.none:
                            default:
                              throw new ArgumentOutOfRangeException();
                          }
                        }
                      }
                    }
                  }
                }

              }
              else if (IsZoopingBigGrid())
              {
                Vector3 startPos = Waypoints[0].Value;
                Vector3 endPos = ClampWallZoopPositionToStartPlane(startPos, currentPos.Value);

                ZoopPlane plane = new ZoopPlane();
                CalculateZoopPlane(startPos, endPos, plane);

                singleItem = startPos == endPos;
                if (singleItem)
                {
                  plane.Count = (direction1: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2), direction2: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2));
                  plane.Increasing = (direction1: startPos.x < endPos.x, direction2: startPos.y < endPos.y);
                  plane.Directions = (direction1: ZoopDirection.x, direction2: ZoopDirection.y);
                }

                await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

                BuildBigStructureList(inventoryManager, plane);

                int structureCounter = 0;

                if (structures.Count > 0)
                {

                  float xOffset = 0;
                  float yOffset = 0;
                  float zOffset = 0;

                  spacing = Mathf.Max(spacing, 1);

                  for (int indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
                  {

                    ZoopDirection zoopDirection2 = plane.Directions.direction2;
                    bool increasing2 = plane.Increasing.direction2;

                    float value2 = increasing2 ? 2f * spacing : -(2f * spacing);
                    switch (zoopDirection2)
                    {
                      case ZoopDirection.x:
                        xOffset = indexDirection2 * value2;
                        break;
                      case ZoopDirection.y:
                        yOffset = indexDirection2 * value2;
                        break;
                      case ZoopDirection.z:
                        zOffset = indexDirection2 * value2;
                        break;
                    }

                    for (int indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
                    {
                      if (structureCounter == structures.Count)
                      {
                        break;
                      }

                      ZoopDirection zoopDirection1 = plane.Directions.direction1;
                      bool increasing1 = plane.Increasing.direction1;

                      float value1 = increasing1 ? 2f * spacing : -(2f * spacing);
                      switch (zoopDirection1)
                      {
                        case ZoopDirection.x:
                          xOffset = indexDirection1 * value1;
                          break;
                        case ZoopDirection.y:
                          yOffset = indexDirection1 * value1;
                          break;
                        case ZoopDirection.z:
                          zOffset = indexDirection1 * value1;
                          break;
                      }

                      Vector3 offset = new Vector3(xOffset, yOffset, zOffset);
                      Vector3 previewPosition = GetBigGridPreviewPosition(startPos, offset);
                      structures[structureCounter].GameObject.SetActive(true);
                      structures[structureCounter].ThingTransformPosition = previewPosition;
                      structures[structureCounter].Position = previewPosition;
                      HasError = HasError || !CanConstructBigCell(structures[structureCounter]);
                      structureCounter++;

                    }
                  }
                }
              }

              foreach (Structure structure in structures)
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
          Debug.Log(e.Message);
          Debug.LogException(e);
        }
      }
    }

    public static void BuildZoop(InventoryManager inventoryManager)
    {

      for (int structureIndex = 0; structureIndex < structures.Count; structureIndex++)
      {
        Structure item = structures[structureIndex];
        PlaceStructure(inventoryManager, item, structureIndex);
      }

      inventoryManager.CancelPlacement();
      CancelZoop();
    }

    public static void AddWaypoint()
    {
      if (InventoryManager.ConstructionCursor is Frame)
      {
        return;
      }
      Vector3? currentPos = GetCurrentMouseGridPosition();
      if (currentPos.HasValue && Waypoints.Last() != currentPos)
      {
        if (structures.Last().GetGridPosition() == currentPos)
        {
          Waypoints.Add(currentPos);
        }
      }
      else if (Waypoints.Last() == currentPos)
      {
        //TODO show message to user that waypoint is already added
      }
    }

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

    private static void PlaceStructure(InventoryManager inventoryManager, Structure item, int structureIndex)
    {
      int buildIndex = ResolveBuildIndex(inventoryManager, item, structureIndex);
      if (buildIndex < 0)
      {
        ZoopMod.Log($"Unable to resolve build index for {item.PrefabName}; skipping zoop placement.", ZoopMod.Logs.error);
        return;
      }

      inventoryManager.ConstructionPanel.BuildIndex = buildIndex;
      InventoryManager.SpawnPrefab = item;
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

      Structure placedStructure = Structure.LastCreatedStructure;
      if (placedStructure?.NextBuildState == null)
      {
        return;
      }

      int lastBuildStateIndex = placedStructure.BuildStates.Count - 1;
      if (lastBuildStateIndex >= 0)
      {
        placedStructure.UpdateBuildStateAndVisualizer(lastBuildStateIndex);
      }
    }

    private static int ResolveBuildIndex(InventoryManager inventoryManager, Structure item, int structureIndex)
    {
      if (structureIndex >= 0 && structureIndex < StructureBuildIndices.Count)
      {
        return StructureBuildIndices[structureIndex];
      }

      int buildIndex = inventoryManager.ConstructionPanel.Parent.Constructables.FindIndex(structure => structure.PrefabName == item.PrefabName);
      if (buildIndex >= 0)
      {
        return buildIndex;
      }

      return inventoryManager.ConstructionPanel.BuildIndex;
    }

    private static void AddStructure(List<Structure> constructables, bool corner, int index, int secondaryCount, ref bool canBuildNext, InventoryManager im, bool supportsCornerVariant)
    {

      int selectedIndex = im.ConstructionPanel.Parent.LastSelectedIndex;
      int straightCount = corner ? secondaryCount : index;
      int cornerCount = corner ? index : secondaryCount;

      Structure activeItem = constructables[selectedIndex];
      if (!corner && supportsCornerVariant)
      {
        switch (activeItem)
        {
          case Pipe or Cable or Frame when selectedIndex != 0:
          case Chute when selectedIndex != 0 && selectedIndex != 2:
            selectedIndex = 0;
            break;
        }
      }

      DynamicThing activeHandItem = InventoryManager.ActiveHandSlot.Get();
      switch (activeHandItem)
      {
        case Stackable constructor:
          bool canMakeItem = activeItem switch
          {
            Chute when selectedIndex == 0 => constructor.Quantity > structures.Count,
            Chute when selectedIndex == 2 => constructor.Quantity > ((straightCount) * 2) + (corner ? 0 : 1) + cornerCount,
            _ => constructor.Quantity > structures.Count
          };

          if (canMakeItem && canBuildNext)
          {
            MakeItem(constructables, corner, index, !corner ? selectedIndex : 1, supportsCornerVariant);
            canBuildNext = true;
          }
          else
          {
            canBuildNext = false;
          }
          break;
        case AuthoringTool:
          MakeItem(constructables, corner, index, !corner ? selectedIndex : 1, supportsCornerVariant);
          canBuildNext = true;
          break;
      }
    }

    private static void ClearStructureCache()
    {
      foreach (Structure structure in structuresCacheStraight)
      {
        structure.gameObject.SetActive(false);
        Object.Destroy(structure);
      }

      structuresCacheStraight.Clear();
      StructureCacheStraightBuildIndices.Clear();

      foreach (Structure structure in structuresCacheCorner)
      {
        structure.gameObject.SetActive(false);
        Object.Destroy(structure);
      }

      structuresCacheCorner.Clear();
      StructureCacheCornerBuildIndices.Clear();
    }

    private static Vector3? GetCurrentMouseGridPosition()
    {
      if (InventoryManager.ConstructionCursor == null)
      {
        return null;
      }

      Vector3 cursorHitPoint = InventoryManager.ConstructionCursor.GetLocalGrid().ToVector3();
      return cursorHitPoint;

    }

    private static void MakeItem(List<Structure> constructables, bool corner, int index, int selectedIndex, bool supportsCornerVariant)
    {
      if (!corner && structuresCacheStraight.Count > index)
      {
        if (!supportsCornerVariant)
        {
          ApplyCursorRotation(structuresCacheStraight[index]);
        }
        structures.Add(structuresCacheStraight[index]);
        StructureBuildIndices.Add(StructureCacheStraightBuildIndices[index]);
      }
      else if (corner && structuresCacheCorner.Count > index)
      {
        if (!supportsCornerVariant)
        {
          ApplyCursorRotation(structuresCacheCorner[index]);
        }
        structures.Add(structuresCacheCorner[index]);
        StructureBuildIndices.Add(StructureCacheCornerBuildIndices[index]);
      }
      else
      {
        Structure structure = constructables[selectedIndex];
        if (structure == null)
        {
          return;
        }

        Structure structureNew = Object.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
        if (structureNew != null)
        {
          structureNew.gameObject.SetActive(true);
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(structureNew);
          }
          structures.Add(structureNew);
          StructureBuildIndices.Add(selectedIndex);
          if (corner)
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
      }
    }

    private static void ApplyCursorRotation(Structure structure)
    {
      if (structure == null || InventoryManager.ConstructionCursor == null)
      {
        return;
      }

      Quaternion rotation = structure is Wall && _zoopStartWallNormal != Vector3.zero
          ? _zoopStartRotation
          : InventoryManager.ConstructionCursor.transform.rotation;

      SetStructureRotation(structure, rotation);
    }

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

    private static Vector3 GetBigGridPreviewPosition(Vector3 startPos, Vector3 offset)
    {
      Vector3 previewPosition = startPos + offset;
      if (InventoryManager.ConstructionCursor is Wall && _zoopStartWallNormal != Vector3.zero)
      {
        previewPosition += _zoopStartWallPositionOffset;
      }

      return previewPosition;
    }

    private static void SetStructureRotation(Structure structure, Quaternion rotation)
    {
      structure.ThingTransformRotation = rotation;
      structure.transform.rotation = rotation;
    }

    private static void SetColor(InventoryManager inventoryManager, Structure structure, bool hasError)
    {
      bool canConstruct = !hasError;
      bool isWaypoint = Waypoints.Contains(structure.Position);
      //check if structure is first element of waypoints
      bool isStart = isWaypoint && Waypoints.First().Equals(structure.Position);
      Color color = canConstruct ? isWaypoint ? isStart ? StartColor : WaypointColor : lineColor : errorColor;
      if (structure is SmallGrid smallGrid)
      {
        List<Connection> list = smallGrid.WillJoinNetwork();
        foreach (Connection openEnd in smallGrid.OpenEnds)
        {
          if (canConstruct)
          {
            Color colorToSet = list.Contains(openEnd) ? Color.yellow.SetAlpha(inventoryManager.CursorAlphaConstructionHelper) : Color.green.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
            foreach (ThingRenderer renderer in smallGrid.Renderers)
            {
              if (renderer.HasRenderer())
              {
                renderer.SetColor(colorToSet);
              }
            }

            foreach (Connection end in smallGrid.OpenEnds)
            {
              end.HelperRenderer.material.color = colorToSet;
            }
          }
          else
          {
            foreach (ThingRenderer renderer in smallGrid.Renderers)
            {
              if (renderer.HasRenderer())
                renderer.SetColor(Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper));
            }

            foreach (Connection end in smallGrid.OpenEnds)
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

    #region Conditionnal Methods
    private static Structure GetSelectedConstructable(InventoryManager inventoryManager)
    {
      List<Structure> constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
      int selectedIndex = inventoryManager.ConstructionPanel.Parent.LastSelectedIndex;
      if (selectedIndex >= 0 && selectedIndex < constructables.Count)
      {
        return constructables[selectedIndex];
      }

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
      SmallCell smallCell = structure.GridController.GetSmallCell(structure.ThingTransformLocalPosition);
      bool invalidStructureExistsOnGrid = smallCell != null &&
                                          (smallCell.Device != null &&
                                              !(structure is Piping pipe && pipe == pipe.IsStraight && smallCell.Device is DevicePipeMounted device && device.contentType == pipe.PipeContentType ||
                                                structure is Cable cable && cable == cable.IsStraight && smallCell.Device is DeviceCableMounted) || smallCell.Other != null);

      bool differentEndsCollision = false;
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
        MethodInfo method = structureType.GetMethod("_IsCollision", BindingFlags.Instance | BindingFlags.NonPublic);

        if (method != null)
        {
          differentEndsCollision = smallCell != null && smallCell.Cable != null && (bool)method.Invoke(structure, [smallCell.Cable]);
          differentEndsCollision |= smallCell != null && smallCell.Pipe != null && (bool)method.Invoke(structure, [smallCell.Pipe]);
          differentEndsCollision |= smallCell != null && smallCell.Chute != null && (bool)method.Invoke(structure, [smallCell.Chute]);
        }

      }

      bool canConstruct = !invalidStructureExistsOnGrid && !differentEndsCollision; // || ZoopMod.CFree;

      if (smallCell != null && smallCell.IsValid() && structure is Piping && smallCell.Pipe is Piping piping)
      {
        Item inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
        CanConstructInfo canReplace = piping.CanReplace(inventoryManager.ConstructionPanel.Parent, inactiveHandOccupant);
        canConstruct &= canReplace.CanConstruct;
      }
      else if (smallCell != null && smallCell.IsValid() && structure is Cable && smallCell.Cable is { } cable2)
      {
        Item inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
        CanConstructInfo canReplace = cable2.CanReplace(inventoryManager.ConstructionPanel.Parent, inactiveHandOccupant);
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
      Cell cell = structure.GridController.GetCell(structure.ThingTransformLocalPosition);
      if (cell != null)
      {
        foreach (Structure cellStructure in cell.AllStructures)
        {
          if (cellStructure is LargeStructure)
          {
            return false;
          }
        }
      }
      return true;
    }

    #endregion

    #region SmallGrid Methods

    private static void CalculateZoopSegments(Vector3 startPos, Vector3 endPos, ZoopSegment segment)
    {
      segment.Directions.Clear();

      float startX = startPos.x;
      float startY = startPos.y;
      float startZ = startPos.z;
      float endX = endPos.x;
      float endY = endPos.y;
      float endZ = endPos.z;

      float absX = Math.Abs(endX - startX);
      float absY = Math.Abs(endY - startY);
      float absZ = Math.Abs(endZ - startZ);

      if (absX > float.Epsilon)
      {
        segment.CountX = 1 + (int)(Math.Abs(startX - endX) * 2);
        segment.IncreasingX = startX < endX;
        UpdateZoopOrder(ZoopDirection.x);
        segment.Directions.Add(ZoopDirection.x);
      }

      if (absY > float.Epsilon)
      {
        segment.CountY = 1 + (int)(Math.Abs(startY - endY) * 2);
        segment.IncreasingY = startY < endY;
        UpdateZoopOrder(ZoopDirection.y);
        segment.Directions.Add(ZoopDirection.y);
      }

      if (absZ > float.Epsilon)
      {
        segment.CountZ = 1 + (int)(Math.Abs(startZ - endZ) * 2);
        segment.IncreasingZ = startZ < endZ;
        UpdateZoopOrder(ZoopDirection.z);
        segment.Directions.Add(ZoopDirection.z);
      }
    }

    private static void BuildSmallStructureList(InventoryManager inventoryManager, List<ZoopSegment> zoops, bool supportsCornerVariant)
    {
      structures.Clear();
      StructureBuildIndices.Clear();
      structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
      structuresCacheCorner.ForEach(structure => structure.GameObject.SetActive(false));

      int straight = 0;
      int corners = 0;
      ZoopDirection lastDirection = ZoopDirection.none;
      bool canBuildNext = true;
      for (int segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++)
      {
        ZoopSegment segment = zoops[segmentIndex];
        for (int directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++)
        {
          ZoopDirection zoopDirection = segment.Directions[directionIndex];
          int zoopCounter = GetCountForDirection(zoopDirection, segment);

          // If it's not the last segment and it's the last direction in the segment, reduce the counter by 1
          if (segmentIndex < zoops.Count - 1 && directionIndex == segment.Directions.Count - 1)
          {
            zoopCounter--;
          }
          else if (directionIndex < segment.Directions.Count - 1)
          {
            zoopCounter--;
          }

          for (int j = 0; j < zoopCounter; j++)
          {
            if (structures.Count > 0 && (j == 0 || segmentIndex > 0) && supportsCornerVariant)
            {
              if (zoopDirection != lastDirection)
              {
                AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, true, corners, straight, ref canBuildNext, inventoryManager, supportsCornerVariant); // start with corner on secondary and tertiary zoop directions
                corners++;
              }
              else
              {
                AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, corners, ref canBuildNext, inventoryManager, supportsCornerVariant);
                straight++;
              }
            }
            else
            {
              AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, corners, ref canBuildNext, inventoryManager, supportsCornerVariant);
              straight++;
            }
            lastDirection = zoopDirection;
          }
        }
      }
    }

    private static bool SupportsCornerVariant(List<Structure> constructables, int selectedIndex)
    {
      if (constructables == null || selectedIndex < 0 || selectedIndex >= constructables.Count)
      {
        return false;
      }

      Structure selectedStructure = constructables[selectedIndex];
      if (selectedStructure == null)
      {
        return false;
      }

      foreach (Structure structure in constructables)
      {
        if (IsCornerVariant(structure) && IsMatchingCornerFamily(selectedStructure, structure))
        {
          return true;
        }
      }

      return false;
    }

    private static bool IsCornerVariant(Structure structure)
    {
      if (structure == null)
      {
        return false;
      }

      string prefabName = structure.GetPrefabName();
      if (!string.IsNullOrEmpty(prefabName) &&
          prefabName.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        return true;
      }

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
      switch (direction)
      {
        case ZoopDirection.x:
          return segment.CountX;
        case ZoopDirection.y:
          return segment.CountY;
        case ZoopDirection.z:
          return segment.CountZ;
        case ZoopDirection.none:
        default:
          throw new ArgumentOutOfRangeException();
      }
    }

    private static bool GetIncreasingForDirection(ZoopDirection direction, ZoopSegment segment)
    {
      switch (direction)
      {
        case ZoopDirection.x:
          return segment.IncreasingX;
        case ZoopDirection.y:
          return segment.IncreasingY;
        case ZoopDirection.z:
          return segment.IncreasingZ;
        case ZoopDirection.none:
        default:
          throw new ArgumentOutOfRangeException();
      }
    }

    #endregion

    #region BigGrid Methods

    private static void CalculateZoopPlane(Vector3 startPos, Vector3 endPos, ZoopPlane plane)
    {

      float startX = startPos.x;
      float startY = startPos.y;
      float startZ = startPos.z;
      float endX = endPos.x;
      float endY = endPos.y;
      float endZ = endPos.z;

      float absX = Math.Abs(endX - startX) / 2;
      float absY = Math.Abs(endY - startY) / 2;
      float absZ = Math.Abs(endZ - startZ) / 2;

      var directions = new List<(float value, ZoopDirection direction, int count, bool increasing)>{
                (absX, ZoopDirection.x, 1 + (int)(Math.Abs(startX - endX)/2), startX < endX),
                (absY, ZoopDirection.y, 1 + (int)(Math.Abs(startY - endY)/2), startY < endY),
                (absZ, ZoopDirection.z, 1 + (int)(Math.Abs(startZ - endZ)/2), startZ < endZ)
            };

      directions.Sort((a, b) => b.value.CompareTo(a.value));

      plane.Directions = (direction1: directions[0].direction, direction2: directions[1].direction);
      plane.Count = (direction1: directions[0].count, direction2: directions[1].count);
      plane.Increasing = (direction1: directions[0].increasing, direction2: directions[1].increasing);
    }

    private static void BuildBigStructureList(InventoryManager inventoryManager, ZoopPlane plane)
    {
      structures.Clear();
      StructureBuildIndices.Clear();
      structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
      int count = 0;
      bool canBuildNext = true;

      for (int indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++)
      {
        for (int indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++)
        {
          AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0, ref canBuildNext, inventoryManager, supportsCornerVariant: false);
          count++;
        }
      }
    }

    private static void UpdateZoopOrder(ZoopDirection direction)
    {
      // add if this direction is not yet in the list
      if (!PreferredZoopOrder.Contains(direction))
      {
        PreferredZoopOrder.Add(direction);
      }
    }

    private static Vector3 GetCardinalAxis(Vector3 vector)
    {
      Vector3 normalized = vector.normalized;
      float xAbs = Mathf.Abs(normalized.x);
      float yAbs = Mathf.Abs(normalized.y);
      float zAbs = Mathf.Abs(normalized.z);

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

    private static void SetCornerRotation(Structure structure, ZoopDirection zoopDirectionFrom, bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo)
    {
      float xOffset = 0.0f;
      float yOffset = 0.0f;
      float zOffset = 0.0f;
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

      SetStructureRotation(structure, ZoopUtils.GetCornerRotation(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo, xOffset, yOffset, zOffset));
    }

    #endregion

  }

}
