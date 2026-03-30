using System.Collections;
using System.Reflection;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.Placement;

internal static class ZoopBuildExecutor
{
  private const int PlacementsPerFrame = 8;

  private static readonly FieldInfo UsePrimaryPositionField =
    typeof(InventoryManager).GetField("_usePrimaryPosition", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly FieldInfo UsePrimaryRotationField =
    typeof(InventoryManager).GetField("_usePrimaryRotation", BindingFlags.Instance | BindingFlags.NonPublic);

  private static readonly MethodInfo UsePrimaryCompleteMethod = typeof(InventoryManager).GetMethod("UsePrimaryComplete",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

  public static IEnumerator BuildAll(
    InventoryManager inventoryManager,
    ZoopBuildPlan buildPlan)
  {
    if (UsePrimaryCompleteMethod == null)
    {
      ZoopLog.Error("[Build] Unable to find InventoryManager.UsePrimaryComplete; skipping zoop placement.");
      yield break;
    }

    for (var structureIndex = 0; structureIndex < buildPlan.Count; structureIndex++)
    {
      var piece = buildPlan.Pieces[structureIndex];
      var buildIndex = piece.BuildIndex;
      inventoryManager.ConstructionPanel.BuildIndex = buildIndex;
      InventoryManager.SpawnPrefab = piece.SpawnPrefab;
      UsePrimaryPositionField?.SetValue(inventoryManager, piece.Position);
      UsePrimaryRotationField?.SetValue(inventoryManager, piece.Rotation);
      UsePrimaryCompleteMethod.Invoke(inventoryManager, null);

      // Finalizes Build State in Authoring mode
      FinalizeAuthoringModePlacement();

      if ((structureIndex + 1) % PlacementsPerFrame == 0 && structureIndex + 1 < buildPlan.Count)
      {
        yield return null;
      }
    }
  }

  private static void FinalizeAuthoringModePlacement()
  {
    if (!InventoryManager.IsAuthoringMode)
    {
      return;
    }

    var placedStructure = Structure.LastCreatedStructure;
    if (placedStructure?.NextBuildState == null)
    {
      return;
    }

    var lastBuildStateIndex = placedStructure.BuildStates.Count - 1;
    if (lastBuildStateIndex < 0)
    {
      return;
    }

    placedStructure.UpdateBuildStateAndVisualizer(lastBuildStateIndex);
  }
}
