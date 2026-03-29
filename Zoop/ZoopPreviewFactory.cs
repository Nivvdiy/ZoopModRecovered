using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Objects.Structures;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace ZoopMod.Zoop;

internal sealed class ZoopPreviewFactory(ZoopSession session)
{
  public void AddStructure(List<Structure> constructables, bool isCorner, int index, int secondaryCount,
    ref bool canBuildNext, InventoryManager inventoryManager, bool supportsCornerVariant)
  {
    var selectedIndex = inventoryManager.ConstructionPanel.Parent.LastSelectedIndex;
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
          Chute when selectedIndex == 0 => constructor.Quantity > session.PreviewCount,
          Chute when selectedIndex == 2 => constructor.Quantity > straightCount * 2 + (isCorner ? 0 : 1) + cornerCount,
          _ => constructor.Quantity > session.PreviewCount
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

  public void ClearStructureCache()
  {
    foreach (var structure in session.StraightCache)
    {
      structure.gameObject.SetActive(false);
      UnityObject.Destroy(structure);
    }

    session.StraightCache.Clear();
    session.StraightCacheBuildIndices.Clear();

    foreach (var structure in session.CornerCache)
    {
      structure.gameObject.SetActive(false);
      UnityObject.Destroy(structure);
    }

    session.CornerCache.Clear();
    session.CornerCacheBuildIndices.Clear();
  }

  public void ResetSmallGridPreviewList()
  {
    session.ClearPreviewPieces();
    session.StraightCache.ForEach(structure => structure.GameObject.SetActive(false));
    session.CornerCache.ForEach(structure => structure.GameObject.SetActive(false));
  }

  public void ResetBigGridPreviewList()
  {
    session.ClearPreviewPieces();
    session.StraightCache.ForEach(structure => structure.GameObject.SetActive(false));
  }

  private void MakeItem(List<Structure> constructables, bool isCorner, int index, int selectedIndex,
    bool supportsCornerVariant)
  {
    switch (isCorner)
    {
      case false when session.StraightCache.Count > index:
        {
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(session.StraightCache[index]);
          }

          AddPreviewPiece(session.StraightCache[index], session.StraightCacheBuildIndices[index]);
          break;
        }
      case true when session.CornerCache.Count > index:
        {
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(session.CornerCache[index]);
          }

          AddPreviewPiece(session.CornerCache[index], session.CornerCacheBuildIndices[index]);
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
          if (structureNew == null)
          {
            return;
          }

          structureNew.gameObject.SetActive(true);
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(structureNew);
          }

          AddPreviewPiece(structureNew, selectedIndex);
          if (isCorner)
          {
            session.CornerCache.Add(structureNew);
            session.CornerCacheBuildIndices.Add(selectedIndex);
          }
          else
          {
            session.StraightCache.Add(structureNew);
            session.StraightCacheBuildIndices.Add(selectedIndex);
          }

          break;
        }
    }
  }

  private void ApplyCursorRotation(Structure structure)
  {
    if (structure == null || InventoryManager.ConstructionCursor == null)
    {
      return;
    }

    var rotation = structure is Wall && session.ZoopStartWallNormal != Vector3.zero
      ? session.ZoopStartRotation
      : InventoryManager.ConstructionCursor.transform.rotation;

    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
  }

  private void AddPreviewPiece(Structure structure, int buildIndex)
  {
    session.PreviewPieces.Add(new PreviewPiece(structure, buildIndex));
  }
}
