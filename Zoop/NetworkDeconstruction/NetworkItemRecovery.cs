using System.Collections.Generic;
using Assets.Scripts.Objects;
using UnityEngine;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Handles item recovery when deconstructing network structures.
/// Spawns items based on structure prefabs.
/// </summary>
public class NetworkItemRecovery
{
  /// <summary>
  /// Spawns items for all structures in the network.
  /// Most structures spawn themselves as items (ItemCableStraight, ItemPipeStraight, etc.)
  /// </summary>
  public void RecoverItemsFromNetwork(List<Structure> network)
  {
    if (network == null || network.Count == 0)
    {
      ZoopLog.Info("[ItemRecovery] No network to recover items from");
      return;
    }

    ZoopLog.Info($"[ItemRecovery] Starting item recovery for {network.Count} structures");

    // Group items by prefab name to spawn them together
    var itemCounts = new Dictionary<string, int>();
    var firstStructurePosition = network[0]?.transform.position ?? Vector3.zero;

    foreach (var structure in network)
    {
      if (structure == null)
        continue;

      // Get the item name from the structure name
      // StructureCableStraight -> ItemCableStraight
      string itemName = GetItemNameFromStructure(structure);

      if (!itemCounts.ContainsKey(itemName))
        itemCounts[itemName] = 0;

      itemCounts[itemName]++;
    }

    // Spawn all items
    int totalSpawned = 0;
    foreach (var kvp in itemCounts)
    {
      string itemName = kvp.Key;
      int count = kvp.Value;

      for (int i = 0; i < count; i++)
      {
        if (SpawnItem(itemName, firstStructurePosition))
        {
          totalSpawned++;
        }
      }
    }

    ZoopLog.Info($"[ItemRecovery] Spawned {totalSpawned} items from {network.Count} structures");

    // Log summary
    foreach (var kvp in itemCounts)
    {
      ZoopLog.Info($"[ItemRecovery]   - {kvp.Value}x {kvp.Key}");
    }
  }

  /// <summary>
  /// Converts structure name to item name.
  /// StructureCableStraight -> ItemCableStraight
  /// StructurePipeStraight -> ItemPipeStraight
  /// </summary>
  private string GetItemNameFromStructure(Structure structure)
  {
    string prefabName = structure.PrefabName;

    // Replace "Structure" with "Item"
    if (prefabName.StartsWith("Structure"))
    {
      return "Item" + prefabName.Substring("Structure".Length);
    }

    // Fallback: return as-is
    return prefabName;
  }

  /// <summary>
  /// Spawns an item by name at the specified position.
  /// </summary>
  private bool SpawnItem(string itemName, Vector3 position)
  {
    try
    {
      // Try to find the prefab by name
      var allPrefabs = Resources.FindObjectsOfTypeAll<Thing>();
      Thing itemPrefab = null;

      foreach (var prefab in allPrefabs)
      {
        if (prefab.PrefabName == itemName)
        {
          itemPrefab = prefab;
          break;
        }
      }

      if (itemPrefab == null)
      {
        ZoopLog.Debug($"[ItemRecovery] Could not find item prefab: {itemName}");
        return false;
      }

      // Spawn with slight random offset
      var spawnPos = position + Random.insideUnitSphere * 0.5f;

      var instance = Object.Instantiate(itemPrefab.gameObject, spawnPos, Quaternion.identity);

      // The item should be initialized automatically by Instantiate
      ZoopLog.Debug($"[ItemRecovery] Spawned {itemName} at {spawnPos}");
      return true;
    }
    catch (System.Exception ex)
    {
      ZoopLog.Error($"[ItemRecovery] Failed to spawn {itemName}: {ex.Message}");
      return false;
    }
  }
}
