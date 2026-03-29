using Assets.Scripts.UI;
using HarmonyLib;
using JetBrains.Annotations;

namespace ZoopMod.Zoop.EntryPoints.Patches;

[HarmonyPatch(typeof(ConstructionPanel), "SelectUp")]
internal static class ConstructionPanelSelectUpPatch
{
  [UsedImplicitly]
  public static bool Prefix()
  {
    return !ZoopRuntime.Controller.IsZoopKeyPressed;
  }
}

[HarmonyPatch(typeof(ConstructionPanel), "SelectDown")]
internal static class ConstructionPanelSelectDownPatch
{
  [UsedImplicitly]
  public static bool Prefix()
  {
    return !ZoopRuntime.Controller.IsZoopKeyPressed;
  }
}
