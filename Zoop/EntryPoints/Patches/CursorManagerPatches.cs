using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Util;
using HarmonyLib;
using JetBrains.Annotations;

namespace ZoopMod.Zoop.EntryPoints.Patches;

[HarmonyPatch(typeof(CursorManager), "SetSelectionColor")]
internal static class CursorManagerSetSelectionColorPatch
{
  [UsedImplicitly]
  public static void Postfix()
  {
    if (ZoopRuntime.Controller.IsZooping)
    {
      CursorManager.CursorSelectionRenderer.material.color =
        ZoopRuntime.Controller.LineColor.SetAlpha(InventoryManager.Instance.CursorAlphaInteractable);
    }
  }
}
