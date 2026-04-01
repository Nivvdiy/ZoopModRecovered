using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Placement;
using Thing = Assets.Scripts.Objects.Thing;
using UnityObject = UnityEngine.Object;

namespace ZoopMod.Zoop.Preview;

internal static class ZoopPreviewFactory
{
  /// <returns>The updated <c>canBuildNext</c> value to carry forward to the next piece.</returns>
  public static bool AddStructure(
    ZoopPreviewContext context,
    bool isCorner,
    int index,
    int secondaryCount,
    bool canBuildNext)
  {
    var draft = context.Draft;
    var constructables = context.Constructables;
    var supportsCornerVariant = context.SupportsCornerVariant;
    var selectedIndex = context.InventoryManager.ConstructionPanel.Parent.LastSelectedIndex;
    var straightCount = isCorner ? secondaryCount : index;
    var cornerCount = isCorner ? index : secondaryCount;
    var buildIndex = ZoopConstructableRules.ResolvePreviewBuildIndex(constructables, selectedIndex,
      isCorner, supportsCornerVariant);
    var activeItem = buildIndex >= 0 && buildIndex < constructables.Count
      ? constructables[buildIndex]
      : null;
    if (activeItem == null)
    {
      return false;
    }

    var activeHandItem = InventoryManager.ActiveHandSlot.Get();
    switch (activeHandItem)
    {
      case Stackable constructor:
        var canMakeItem = activeItem switch
        {
          Chute when buildIndex == 0 => constructor.Quantity > draft.TotalCellCost,
          Chute when buildIndex == 2 => constructor.Quantity >
                                        straightCount * 2 + (isCorner ? 0 : 1) + cornerCount,
          _ => constructor.Quantity > draft.TotalCellCost
        };

        if (canMakeItem && canBuildNext)
        {
          MakeItem(context.Draft, context.PreviewCache, constructables, index, buildIndex, supportsCornerVariant);
          return true;
        }

        return false;

      case AuthoringTool:
        MakeItem(context.Draft, context.PreviewCache, constructables, index, buildIndex, supportsCornerVariant);
        return true;

      default:
        return canBuildNext;
    }
  }

  public static void ClearStructureCache(ZoopPreviewCache previewCache)
  {
    foreach (var structure in previewCache.StraightCache)
    {
      structure.gameObject.SetActive(false);
      UnityObject.Destroy(structure);
    }

    previewCache.StraightCache.Clear();
    previewCache.StraightCacheBuildIndices.Clear();

    foreach (var structure in previewCache.CornerCache)
    {
      structure.gameObject.SetActive(false);
      UnityObject.Destroy(structure);
    }

    previewCache.CornerCache.Clear();
    previewCache.CornerCacheBuildIndices.Clear();

    foreach (var kvp in previewCache.LongCaches)
    {
      foreach (var structure in kvp.Value)
      {
        structure.gameObject.SetActive(false);
        UnityObject.Destroy(structure);
      }
    }

    previewCache.LongCaches.Clear();
    previewCache.LongCacheBuildIndices.Clear();
  }

  public static void ResetSmallGridPreviewList(ZoopDraft draft, ZoopPreviewCache previewCache)
  {
    draft.ClearPreviewPieces();
    previewCache.StraightCache.ForEach(structure => structure.GameObject.SetActive(false));
    previewCache.CornerCache.ForEach(structure => structure.GameObject.SetActive(false));
    foreach (var kvp in previewCache.LongCaches)
    {
      kvp.Value.ForEach(structure => structure.GameObject.SetActive(false));
    }
  }

  public static void ResetBigGridPreviewList(ZoopDraft draft, ZoopPreviewCache previewCache)
  {
    draft.ClearPreviewPieces();
    previewCache.StraightCache.ForEach(structure => structure.GameObject.SetActive(false));
  }

  private static void MakeItem(ZoopDraft draft, ZoopPreviewCache previewCache, List<Structure> constructables,
    int index, int selectedIndex,
    bool supportsCornerVariant)
  {
    var isCorner = selectedIndex == 1 && supportsCornerVariant;
    switch (isCorner)
    {
      case false when previewCache.StraightCache.Count > index:
        {
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(draft, previewCache.StraightCache[index]);
          }

          AddPreviewPiece(draft, previewCache.StraightCache[index], previewCache.StraightCacheBuildIndices[index]);
          break;
        }
      case true when previewCache.CornerCache.Count > index:
        {
          AddPreviewPiece(draft, previewCache.CornerCache[index], previewCache.CornerCacheBuildIndices[index]);
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

          // Deactivate the clone so OnDisable fires on all components and deregisters
          // them from the game's global tick lists (Thing.AllThings, network registries, etc.).
          structureNew.gameObject.SetActive(false);

          // Disable game-logic MonoBehaviours (Thing and its derivatives: Structure, SmallGrid, Wall, etc.)
          // to prevent their OnEnable from re-registering in global tick lists when we reactivate.
          foreach (var thing in structureNew.GetComponentsInChildren<Thing>(true))
          {
            thing.enabled = false;
          }

          // Disable colliders to remove them from the physics broadphase.
          foreach (var col in structureNew.GetComponentsInChildren<Collider>(true))
          {
            col.enabled = false;
          }

          // Reactivate so visual components (Wireframe, etc.) run their OnEnable and set up
          // blueprint materials, renderer states, and child object visibility correctly.
          structureNew.gameObject.SetActive(true);

          // Now disable all remaining MonoBehaviours to stop per-frame Update/LateUpdate
          // calls. The visual state is already initialized from OnEnable above.
          foreach (var mb in structureNew.GetComponentsInChildren<MonoBehaviour>(true))
          {
            mb.enabled = false;
          }
          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(draft, structureNew);
          }

          AddPreviewPiece(draft, structureNew, selectedIndex);
          if (isCorner)
          {
            previewCache.CornerCache.Add(structureNew);
            previewCache.CornerCacheBuildIndices.Add(selectedIndex);
          }
          else
          {
            previewCache.StraightCache.Add(structureNew);
            previewCache.StraightCacheBuildIndices.Add(selectedIndex);
          }

          break;
        }
    }
  }

  private static void ApplyCursorRotation(ZoopDraft draft, Structure structure)
  {
    if (structure == null || InventoryManager.ConstructionCursor == null || draft == null)
    {
      return;
    }

    var rotation = structure is Wall && draft.ZoopStartWallNormal != Vector3.zero
      ? draft.ZoopStartRotation
      : InventoryManager.ConstructionCursor.transform.rotation;

    structure.ThingTransformRotation = rotation;
    structure.transform.rotation = rotation;
  }

  /// <returns>The updated <c>canBuildNext</c> value to carry forward to the next piece.</returns>
  public static bool AddLongStructure(
    ZoopPreviewContext context,
    int longBuildIndex,
    int cellSpan,
    int longIndex,
    bool canBuildNext)
  {
    var draft = context.Draft;
    var constructables = context.Constructables;
    var activeItem = longBuildIndex >= 0 && longBuildIndex < constructables.Count
      ? constructables[longBuildIndex]
      : null;
    if (activeItem == null)
    {
      return false;
    }

    var activeHandItem = InventoryManager.ActiveHandSlot.Get();
    switch (activeHandItem)
    {
      case Stackable constructor:
        if (constructor.Quantity > draft.TotalCellCost + cellSpan - 1 && canBuildNext)
        {
          MakeLongItem(draft, context.PreviewCache, constructables, longIndex, longBuildIndex, cellSpan,
            context.SupportsCornerVariant);
          return true;
        }

        return false;

      case AuthoringTool:
        MakeLongItem(draft, context.PreviewCache, constructables, longIndex, longBuildIndex, cellSpan,
          context.SupportsCornerVariant);
        return true;

      default:
        return canBuildNext;
    }
  }

  private static void MakeLongItem(ZoopDraft draft, ZoopPreviewCache previewCache,
    List<Structure> constructables, int longIndex, int buildIndex, int cellSpan,
    bool supportsCornerVariant)
  {
    if (!previewCache.LongCaches.TryGetValue(cellSpan, out var cache))
    {
      cache = new List<Structure>();
      previewCache.LongCaches[cellSpan] = cache;
      previewCache.LongCacheBuildIndices[cellSpan] = new List<int>();
    }

    var indexCache = previewCache.LongCacheBuildIndices[cellSpan];

    if (cache.Count > longIndex)
    {
      if (!supportsCornerVariant)
      {
        ApplyCursorRotation(draft, cache[longIndex]);
      }

      AddPreviewPiece(draft, cache[longIndex], indexCache[longIndex], cellSpan);
      return;
    }

    var structure = constructables[buildIndex];
    if (structure == null)
    {
      return;
    }

    var structureNew = UnityObject.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
    if (structureNew == null)
    {
      return;
    }

    structureNew.gameObject.SetActive(false);
    foreach (var thing in structureNew.GetComponentsInChildren<Thing>(true))
    {
      thing.enabled = false;
    }

    foreach (var col in structureNew.GetComponentsInChildren<Collider>(true))
    {
      col.enabled = false;
    }

    structureNew.gameObject.SetActive(true);
    foreach (var mb in structureNew.GetComponentsInChildren<MonoBehaviour>(true))
    {
      mb.enabled = false;
    }

    if (!supportsCornerVariant)
    {
      ApplyCursorRotation(draft, structureNew);
    }

    AddPreviewPiece(draft, structureNew, buildIndex, cellSpan);
    cache.Add(structureNew);
    indexCache.Add(buildIndex);
  }

  private static void AddPreviewPiece(ZoopDraft draft, Structure structure, int buildIndex, int cellSpan = 1)
  {
    draft.PreviewPieces.Add(new PreviewPiece(structure, buildIndex, cellSpan));
    draft.TotalCellCost += cellSpan;
  }
}
