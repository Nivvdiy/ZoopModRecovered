using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Placement;

internal static class ZoopConstructableResolver
{
  public static int ResolveBuildIndex(ZoopDraft draft, InventoryManager inventoryManager, Structure item,
    int structureIndex)
  {
    if (draft != null && structureIndex >= 0 && structureIndex < draft.PreviewCount)
    {
      return draft.PreviewPieces[structureIndex].BuildIndex;
    }

    if (TryGetConstructables(inventoryManager, out var constructables, out _))
    {
      var buildIndex = constructables.FindIndex(structure =>
        structure != null && item != null && structure.PrefabName == item.PrefabName);
      if (buildIndex >= 0)
      {
        return buildIndex;
      }
    }

    return inventoryManager?.ConstructionPanel?.BuildIndex ?? -1;
  }

  public static Structure GetConstructableForBuildIndex(InventoryManager inventoryManager, int buildIndex)
  {
    return TryGetConstructables(inventoryManager, out var constructables, out _) &&
           buildIndex >= 0 &&
           buildIndex < constructables.Count
      ? constructables[buildIndex]
      : null;
  }

  public static Structure GetSelectedConstructable(InventoryManager inventoryManager)
  {
    if (TryGetConstructables(inventoryManager, out var constructables, out var selectedIndex) &&
        selectedIndex >= 0 &&
        selectedIndex < constructables.Count)
    {
      return constructables[selectedIndex];
    }

    return InventoryManager.ConstructionCursor;
  }

  private static bool TryGetConstructables(InventoryManager inventoryManager, out List<Structure> constructables,
    out int selectedIndex)
  {
    constructables = null;
    selectedIndex = -1;

    var constructionPanel = inventoryManager?.ConstructionPanel;
    var parent = constructionPanel?.Parent;
    if (parent?.Constructables == null)
    {
      return false;
    }

    constructables = parent.Constructables;
    selectedIndex = parent.LastSelectedIndex;
    return true;
  }
}
