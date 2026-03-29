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
    ZoopBuildPlan buildPlan)
  {
    for (var structureIndex = 0; structureIndex < buildPlan.Count; structureIndex++)
    {
      var piece = buildPlan.Pieces[structureIndex];
      var buildIndex = piece.BuildIndex;
      inventoryManager.ConstructionPanel.BuildIndex = buildIndex;
      InventoryManager.SpawnPrefab = piece.SpawnPrefab;
      UsePrimaryPositionField?.SetValue(inventoryManager, piece.Position);
      UsePrimaryRotationField?.SetValue(inventoryManager, piece.Rotation);
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
