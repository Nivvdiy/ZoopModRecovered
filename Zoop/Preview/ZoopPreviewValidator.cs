using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Util;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Placement;

namespace ZoopMod.Zoop.Preview;

internal sealed class ZoopPreviewValidator(ZoopPlacementUpdateGate placementUpdateGate)
{
  private readonly struct ValidationCursorState(
    int originalBuildIndex,
    int validationBuildIndex,
    bool needsCursorSwap,
    Vector3 originalCursorPosition,
    Vector3 originalCursorLocalPosition,
    Quaternion originalCursorRotation,
    Quaternion originalCursorLocalRotation)
  {
    public int OriginalBuildIndex { get; } = originalBuildIndex;
    public int ValidationBuildIndex { get; } = validationBuildIndex;
    public bool NeedsCursorSwap { get; } = needsCursorSwap;
    public Vector3 OriginalCursorPosition { get; } = originalCursorPosition;
    public Vector3 OriginalCursorLocalPosition { get; } = originalCursorLocalPosition;
    public Quaternion OriginalCursorRotation { get; } = originalCursorRotation;
    public Quaternion OriginalCursorLocalRotation { get; } = originalCursorLocalRotation;
  }

  public bool CanConstructSmallCell(ZoopDraft draft, InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return CanConstructWithValidationCursor(draft, inventoryManager, structure, structureIndex,
      validationCursor => ValidateSmallCellCursor(inventoryManager, validationCursor));
  }

  public bool CanConstructBigCell(ZoopDraft draft, InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return CanConstructWithValidationCursor(draft, inventoryManager, structure, structureIndex, ValidateLargeGridCursor);
  }

  private bool CanConstructWithValidationCursor(ZoopDraft draft, InventoryManager inventoryManager, Structure structure, int structureIndex,
    Func<Structure, bool> validator)
  {
    var originalCursor = InventoryManager.ConstructionCursor;
    if (originalCursor == null)
    {
      return false;
    }

    if (!TryGetValidationTarget(draft, inventoryManager, structure, structureIndex, originalCursor,
          out var validationConstructable, out var cursorState))
    {
      return false;
    }

    try
    {
      using (placementUpdateGate.BeginScope())
      {
        var validationCursor = PrepareValidationCursor(inventoryManager, structure, validationConstructable, cursorState);
        if (validationCursor == null)
        {
          return false;
        }

        return validator(validationCursor);
      }
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

  private static bool TryGetValidationTarget(ZoopDraft draft, InventoryManager inventoryManager, Structure structure, int structureIndex,
    Structure originalCursor, out Structure validationConstructable, out ValidationCursorState cursorState)
  {
    var originalBuildIndex = inventoryManager.ConstructionPanel.BuildIndex;
    var validationBuildIndex = ZoopConstructableResolver.ResolveBuildIndex(draft, inventoryManager, structure, structureIndex);
    validationConstructable = ZoopConstructableResolver.GetConstructableForBuildIndex(inventoryManager, validationBuildIndex);
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

  private static bool ValidateSmallCellCursor(InventoryManager inventoryManager, Structure validationCursor)
  {
    var canConstruct = validationCursor.CanConstruct().CanConstruct;
    if (!canConstruct || InventoryManager.IsAuthoringMode || validationCursor is not IGridMergeable mergeable)
    {
      return canConstruct;
    }

    var activeConstructor = InventoryManager.Parent.Slots[inventoryManager.ActiveHand.SlotId].Get() as MultiConstructor;
    var inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
    return mergeable.CanReplace(activeConstructor, inactiveHandOccupant).CanConstruct;
  }

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

  private static void RestoreValidationCursor(InventoryManager inventoryManager, ValidationCursorState cursorState)
  {
    if (cursorState.NeedsCursorSwap)
    {
      inventoryManager.ConstructionPanel.BuildIndex = cursorState.OriginalBuildIndex;
      var originalConstructable = ZoopConstructableResolver.GetConstructableForBuildIndex(inventoryManager, cursorState.OriginalBuildIndex);
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
}
