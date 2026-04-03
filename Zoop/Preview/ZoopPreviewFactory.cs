using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Placement;
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
          Chute when buildIndex == 0 => constructor.Quantity > draft.TotalResourceCost,
          Chute when buildIndex == 2 => constructor.Quantity >
                                        straightCount * 2 + (isCorner ? 0 : 1) + cornerCount,
          _ => constructor.Quantity > draft.TotalResourceCost
        };

        if (canMakeItem && canBuildNext)
        {
          PreparePreviewPiece(context.Draft, context.PreviewCache, constructables, index, buildIndex, supportsCornerVariant);
          return true;
        }

        return false;

      case AuthoringTool:
        PreparePreviewPiece(context.Draft, context.PreviewCache, constructables, index, buildIndex, supportsCornerVariant);
        return true;

      default:
        return canBuildNext;
    }
  }

  public static void ClearStructureCache(ZoopPreviewCache previewCache)
  {
    foreach (var entry in previewCache.StraightCache)
    {
      entry.Instance.gameObject.SetActive(false);
      UnityObject.Destroy(entry.Instance);
    }
    previewCache.StraightCache.Clear();

    foreach (var entry in previewCache.CornerCache)
    {
      entry.Instance.gameObject.SetActive(false);
      UnityObject.Destroy(entry.Instance);
    }
    previewCache.CornerCache.Clear();

    foreach (var kvp in previewCache.LongCaches)
    {
      foreach (var entry in kvp.Value)
      {
        entry.Instance.gameObject.SetActive(false);
        UnityObject.Destroy(entry.Instance);
      }
    }
    previewCache.LongCaches.Clear();
  }

  public static void ResetSmallGridPreviewList(ZoopDraft draft, ZoopPreviewCache previewCache)
  {
    draft.ClearPreviewPieces();
    previewCache.StraightCache.ForEach(e => e.Instance.GameObject.SetActive(false));
    previewCache.CornerCache.ForEach(e => e.Instance.GameObject.SetActive(false));
    foreach (var kvp in previewCache.LongCaches)
      kvp.Value.ForEach(e => e.Instance.GameObject.SetActive(false));
  }

  public static void ResetBigGridPreviewList(ZoopDraft draft, ZoopPreviewCache previewCache)
  {
    draft.ClearPreviewPieces();
    previewCache.StraightCache.ForEach(e => e.Instance.GameObject.SetActive(false));
  }

  private static void PreparePreviewPiece(ZoopDraft draft, ZoopPreviewCache previewCache, List<Structure> constructables,
    int index, int selectedIndex,
    bool supportsCornerVariant)
  {
    var isCorner = selectedIndex == 1 && supportsCornerVariant;
    switch (isCorner)
    {
      case false when previewCache.StraightCache.Count > index:
        {
          var cached = previewCache.StraightCache[index];
          if (!supportsCornerVariant)
            ApplyCursorRotation(draft, cached.Instance);
          AddPreviewPiece(draft, cached.Instance, cached.BuildIndex);
          break;
        }
      case true when previewCache.CornerCache.Count > index:
        {
          var cached = previewCache.CornerCache[index];
          AddPreviewPiece(draft, cached.Instance, cached.BuildIndex);
          break;
        }
      default:
        {
          var structure = constructables[selectedIndex];
          if (structure == null)
          {
            return;
          }

          var structureNew = InstantiatePreviewClone(structure);
          if (structureNew == null)
          {
            return;
          }

          if (!supportsCornerVariant)
          {
            ApplyCursorRotation(draft, structureNew);
          }

          AddPreviewPiece(draft, structureNew, selectedIndex);
          if (isCorner)
          {
            previewCache.CornerCache.Add(new CachedStructure(structureNew, selectedIndex));
          }
          else
          {
            previewCache.StraightCache.Add(new CachedStructure(structureNew, selectedIndex));
          }

          break;
        }
    }
  }

  // Instantiates a disabled preview clone of structure's cursor prefab, strips game-logic
  // components, and re-enables only visuals. Returns null when the prefab cannot be found.
  // Detail: deactivate first so OnDisable fires and deregisters from Thing.AllThings etc.,
  // disable Things + Colliders, reactivate so visual OnEnable runs (blueprint materials etc.),
  // then disable all remaining MonoBehaviours to suppress per-frame Update calls.
  private static Structure InstantiatePreviewClone(Structure structure)
  {
    var clone = UnityObject.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
    if (clone == null) return null;

    clone.gameObject.SetActive(false);
    foreach (var thing in clone.GetComponentsInChildren<Thing>(true)) thing.enabled = false;
    foreach (var col in clone.GetComponentsInChildren<Collider>(true)) col.enabled = false;
    clone.gameObject.SetActive(true);
    foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

    return clone;
  }

  private static void ApplyCursorRotation(ZoopDraft draft, Structure structure)
  {
    if (structure == null || InventoryManager.ConstructionCursor == null || draft == null)
    {
      return;
    }

    var rotation = structure is Wall && draft.Session.StartWallNormal != Vector3.zero
      ? draft.Session.StartRotation
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
        var nextLongCost = GetEntryQuantity(activeItem);
        if (constructor.Quantity > draft.TotalResourceCost + nextLongCost - 1 && canBuildNext)
        {
          PrepareLongPreviewPiece(draft, context.PreviewCache, constructables, longIndex, longBuildIndex, cellSpan,
            context.SupportsCornerVariant);
          return true;
        }

        return false;

      case AuthoringTool:
        PrepareLongPreviewPiece(draft, context.PreviewCache, constructables, longIndex, longBuildIndex, cellSpan,
          context.SupportsCornerVariant);
        return true;

      default:
        return canBuildNext;
    }
  }

  private static void PrepareLongPreviewPiece(ZoopDraft draft, ZoopPreviewCache previewCache,
    List<Structure> constructables, int longIndex, int buildIndex, int cellSpan,
    bool supportsCornerVariant)
  {
    if (!previewCache.LongCaches.TryGetValue(cellSpan, out var cache))
    {
      cache = new List<CachedStructure>();
      previewCache.LongCaches[cellSpan] = cache;
    }

    if (cache.Count > longIndex)
    {
      var cached = cache[longIndex];
      if (!supportsCornerVariant)
        ApplyCursorRotation(draft, cached.Instance);
      AddPreviewPiece(draft, cached.Instance, cached.BuildIndex, cellSpan);
      return;
    }

    var structure = constructables[buildIndex];
    if (structure == null)
    {
      return;
    }

    var structureNew = InstantiatePreviewClone(structure);
    if (structureNew == null)
    {
      return;
    }

    if (!supportsCornerVariant)
    {
      ApplyCursorRotation(draft, structureNew);
    }

    AddPreviewPiece(draft, structureNew, buildIndex, cellSpan);
    cache.Add(new CachedStructure(structureNew, buildIndex));
  }

  private static void AddPreviewPiece(ZoopDraft draft, Structure structure, int buildIndex, int cellSpan = 1)
  {
    draft.PreviewPieces.Add(new PreviewPiece(structure, buildIndex, cellSpan));
    draft.TotalCellCost += cellSpan;
    draft.TotalResourceCost += GetEntryQuantity(structure);
  }

  internal static int GetEntryQuantity(Structure structure)
  {
    if (structure.BuildStates != null && structure.BuildStates.Count > 0)
    {
      return structure.BuildStates[0].Tool.EntryQuantity;
    }

    return 1;
  }
}
