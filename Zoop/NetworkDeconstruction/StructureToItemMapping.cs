using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Maps structure prefab names to their corresponding item prefab names.
/// Cables return ItemCableCoil, pipes return individual items, chutes return individual items.
/// </summary>
public static class StructureToItemMapping
{
  /// <summary>
  /// Gets the item name and quantity for a specific structure instance.
  /// Takes into account cable types and pipe types.
  /// </summary>
  public static ItemInfo GetItemInfo(Structure structure)
  {
    // Handle cables - type matters (normal, heavy, superHeavy)
    if (structure is Cable cable)
    {
      return GetCableItemInfo(cable);
    }

    // Handle pipes - type matters
    if (structure is Pipe pipe)
    {
      return GetPipeItemInfo(pipe);
    }

    // Handle chutes - individual items
    if (structure is Chute)
    {
      return GetChuteItemInfo(structure.PrefabName);
    }

    // Fallback
    return new ItemInfo(structure.PrefabName, 1);
  }

  private static ItemInfo GetCableItemInfo(Cable cable)
  {
    // All cables return ItemCableCoil regardless of type
    // But we should check the type to log it
    int quantity = GetQuantityForCableStructure(cable.PrefabName);
    return new ItemInfo("ItemCableCoil", quantity, cable.CableType.ToString());
  }

  private static ItemInfo GetPipeItemInfo(Pipe pipe)
  {
    // Pipes return individual items (ItemPipeStraight, etc.) not coils
    string itemName = pipe.PrefabName.Replace("Structure", "Item");
    int quantity = 1;

    return new ItemInfo(itemName, quantity, pipe.PipeType.ToString());
  }

  private static ItemInfo GetChuteItemInfo(string prefabName)
  {
    // Chutes return individual items
    string itemName = prefabName.Replace("Structure", "Item");
    return new ItemInfo(itemName, 1);
  }

  private static int GetQuantityForCableStructure(string prefabName)
  {
    // Cable quantity based on structure complexity
    if (prefabName.Contains("Straight") || prefabName.Contains("Corner"))
      return 1;
    if (prefabName.Contains("Junction3"))
      return 2;
    if (prefabName.Contains("Junction4"))
      return 3;
    if (prefabName.Contains("Junction5"))
      return 4;
    if (prefabName.Contains("Junction6"))
      return 5;

    return 1;
  }

  public class ItemInfo
  {
    public string ItemName { get; }
    public int Quantity { get; }
    public string TypeInfo { get; }

    public ItemInfo(string itemName, int quantity, string typeInfo = null)
    {
      ItemName = itemName;
      Quantity = quantity;
      TypeInfo = typeInfo;
    }
  }
}
