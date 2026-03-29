using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Placement;

internal static class ZoopConstructableResolver
{
  public static int ResolveBuildIndex(ZoopDraft draft, InventoryManager inventoryManager, Structure item, int structureIndex)
  {
    if (draft != null && structureIndex >= 0 && structureIndex < draft.PreviewCount)
    {
      return draft.PreviewPieces[structureIndex].BuildIndex;
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

  public static Structure GetConstructableForBuildIndex(InventoryManager inventoryManager, int buildIndex)
  {
    var constructables = inventoryManager.ConstructionPanel.Parent.Constructables;
    return buildIndex >= 0 && buildIndex < constructables.Count
      ? constructables[buildIndex]
      : null;
  }
}
