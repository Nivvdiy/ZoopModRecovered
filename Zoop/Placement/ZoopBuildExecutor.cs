using System.Reflection;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.Placement;

internal static class ZoopBuildExecutor
{
  private static readonly FieldInfo UsePrimaryPositionField =
    typeof(InventoryManager).GetField("_usePrimaryPosition", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly FieldInfo UsePrimaryRotationField =
    typeof(InventoryManager).GetField("_usePrimaryRotation", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly MethodInfo UsePrimaryCompleteMethod = typeof(InventoryManager).GetMethod("UsePrimaryComplete",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

  public static void BuildAll(
    InventoryManager inventoryManager,
    ZoopSession session)
  {
    for (var structureIndex = 0; structureIndex < session.PreviewCount; structureIndex++)
    {
      var item = session.PreviewPieces[structureIndex].Structure;
      var buildIndex = ZoopConstructableResolver.ResolveBuildIndex(session, inventoryManager, item, structureIndex);
      if (buildIndex < 0)
      {
        ZoopLog.Error($"[Build] Unable to resolve build index for {item.PrefabName}; skipping zoop placement.");
        continue;
      }

      inventoryManager.ConstructionPanel.BuildIndex = buildIndex;
      InventoryManager.SpawnPrefab = InventoryManager.IsAuthoringMode && session.ZoopSpawnPrefab != null
        ? session.ZoopSpawnPrefab
        : item;
      UsePrimaryPositionField?.SetValue(inventoryManager, item.transform.position);
      UsePrimaryRotationField?.SetValue(inventoryManager, item.transform.rotation);
      if (UsePrimaryCompleteMethod == null)
      {
        ZoopLog.Error("[Build] Unable to find InventoryManager.UsePrimaryComplete; skipping zoop placement.");
        continue;
      }

      UsePrimaryCompleteMethod.Invoke(inventoryManager, null);

      if (!InventoryManager.IsAuthoringMode)
      {
        continue;
      }

      var placedStructure = Structure.LastCreatedStructure;
      if (placedStructure?.NextBuildState == null)
      {
        continue;
      }

      var lastBuildStateIndex = placedStructure.BuildStates.Count - 1;
      if (lastBuildStateIndex >= 0)
      {
        placedStructure.UpdateBuildStateAndVisualizer(lastBuildStateIndex);
      }
    }
  }
}
