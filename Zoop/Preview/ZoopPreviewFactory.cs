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

internal sealed class ZoopPreviewFactory
{
  internal sealed class AddStructureRequest
  {
    public ZoopDraft Draft { get; set; }
    public ZoopPreviewCache PreviewCache { get; set; }
    public List<Structure> Constructables { get; set; }
    public bool IsCorner { get; set; }
    public int Index { get; set; }
    public int SecondaryCount { get; set; }
    public bool CanBuildNext { get; set; }
    public InventoryManager InventoryManager { get; set; }
    public bool SupportsCornerVariant { get; set; }
  }

  public static void AddStructure(AddStructureRequest request)
  {
    var selectedIndex = request.InventoryManager.ConstructionPanel.Parent.LastSelectedIndex;
    var straightCount = request.IsCorner ? request.SecondaryCount : request.Index;
    var cornerCount = request.IsCorner ? request.Index : request.SecondaryCount;
    var buildIndex = ZoopConstructableRules.ResolvePreviewBuildIndex(request.Constructables, selectedIndex,
      request.IsCorner, request.SupportsCornerVariant);
    var activeItem = buildIndex >= 0 && buildIndex < request.Constructables.Count
      ? request.Constructables[buildIndex]
      : null;
    if (activeItem == null)
    {
      request.CanBuildNext = false;
      return;
    }

    var activeHandItem = InventoryManager.ActiveHandSlot.Get();
    switch (activeHandItem)
    {
      case Stackable constructor:
        var canMakeItem = activeItem switch
        {
          Chute when buildIndex == 0 => constructor.Quantity > request.Draft.PreviewCount,
          Chute when buildIndex == 2 => constructor.Quantity >
                                        straightCount * 2 + (request.IsCorner ? 0 : 1) + cornerCount,
          _ => constructor.Quantity > request.Draft.PreviewCount
        };

        if (canMakeItem && request.CanBuildNext)
        {
          MakeItem(request.Draft, request.PreviewCache, request.Constructables, request.Index, buildIndex,
            request.SupportsCornerVariant);
          request.CanBuildNext = true;
        }
        else
        {
          request.CanBuildNext = false;
        }

        break;
      case AuthoringTool:
        MakeItem(request.Draft, request.PreviewCache, request.Constructables, request.Index, buildIndex,
          request.SupportsCornerVariant);
        request.CanBuildNext = true;
        break;
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
  }

  public static void ResetSmallGridPreviewList(ZoopDraft draft, ZoopPreviewCache previewCache)
  {
    draft.ClearPreviewPieces();
    previewCache.StraightCache.ForEach(structure => structure.GameObject.SetActive(false));
    previewCache.CornerCache.ForEach(structure => structure.GameObject.SetActive(false));
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

          structureNew.gameObject.SetActive(true);
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

  private static void AddPreviewPiece(ZoopDraft draft, Structure structure, int buildIndex)
  {
    draft.PreviewPieces.Add(new PreviewPiece(structure, buildIndex));
  }
}
