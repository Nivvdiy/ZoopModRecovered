using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;
using JetBrains.Annotations;
using ZoopMod.Zoop.EntryPoints.Configuration;
using ZoopMod.Zoop.EntryPoints.Input;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.EntryPoints.Patches;

[HarmonyPatch(typeof(InventoryManager), "SetMultiConstructorItemPlacement")]
internal static class InventoryManagerSetMultiConstructPatch
{
  [UsedImplicitly]
  public static void Prefix(InventoryManager __instance, MultiConstructor multiConstructorItem)
  {
    if (ZoopRuntime.Controller.IsZooping)
    {
      ZoopLog.Debug($"[Placement] Detected multi-constructor {multiConstructorItem.PrefabHash}.");
      ZoopRuntime.Controller.RestartZoop(__instance);
    }
  }
}

[HarmonyPatch(typeof(InventoryManager), "SetConstructorItemPlacement")]
internal static class InventoryManagerSetConstructPatch
{
  [UsedImplicitly]
  public static void Prefix(InventoryManager __instance, Constructor constructorItem)
  {
    if (ZoopRuntime.Controller.IsZooping)
    {
      ZoopLog.Debug($"[Placement] Detected constructor {constructorItem.PrefabHash}.");
      ZoopRuntime.Controller.RestartZoop(__instance);
    }
  }
}

[HarmonyPatch(typeof(InventoryManager), "CancelPlacement")]
internal static class InventoryManagerCancelPlacementPatch
{
  [UsedImplicitly]
  public static void Prefix(InventoryManager __instance)
  {
    if (ZoopRuntime.Controller.IsZooping)
    {
      ZoopLog.Debug("[Placement] Zoop canceled during CancelPlacement.");
      ZoopRuntime.Controller.CancelZoop();
      ZoopRuntime.Controller.IsZoopKeyPressed = false;
    }
  }
}

[HarmonyPatch(typeof(InventoryManager), "UpdatePlacement", typeof(Constructor))]
internal static class InventoryManagerUpdatePlacementConstructorPatch
{
  [UsedImplicitly]
  public static bool Prefix(InventoryManager __instance)
  {
    return !ZoopRuntime.Controller.IsZooping || ZoopRuntime.Controller.AllowPlacementUpdate;
  }
}

[HarmonyPatch(typeof(InventoryManager), "UpdatePlacement", typeof(Structure))]
internal static class InventoryManagerUpdatePlacementStructurePatch
{
  [UsedImplicitly]
  public static bool Prefix(InventoryManager __instance)
  {
    return !ZoopRuntime.Controller.IsZooping || ZoopRuntime.Controller.AllowPlacementUpdate;
  }
}

[HarmonyPatch(typeof(InventoryManager), "WaitUntilDone", typeof(InventoryManager.DelegateEvent), typeof(float),
  typeof(Structure))]
internal static class InventoryManagerWaitUntilDonePatch
{
  [UsedImplicitly]
  public static void Prefix(InventoryManager __instance, InventoryManager.DelegateEvent onFinished,
    ref float timeToWait, Structure structure)
  {
    if (!ZoopRuntime.Controller.IsZooping)
    {
      return;
    }

    if (!InventoryManager.IsAuthoringMode)
    {
      var structureCount = Math.Max(ZoopRuntime.Controller.PreviewCount, 1);
      var timeToWaitMultiplier = Math.Max(1, ZoopConfig.MaxZoopWaitTimeMultiplier.Value);
      timeToWait *= Math.Min(structureCount, timeToWaitMultiplier);
    }
    else
    {
      timeToWait = 0f;
    }
  }
}

[HarmonyPatch(typeof(InventoryManager), "PlacementMode")]
internal static class InventoryManagerPlacementModePatch
{
  [UsedImplicitly]
  public static bool Prefix(InventoryManager __instance)
  {
    ZoopRuntime.Controller.IsZoopKeyPressed = KeyManager.GetButton(ZoopKeyBindings.Hold);
    var secondary = KeyManager.GetMouseDown("Secondary");
    var primary = KeyManager.GetMouseDown("Primary");
    var spec = KeyManager.GetButtonDown(ZoopKeyBindings.Switch);
    var addWaypoint = KeyManager.GetButtonDown(ZoopKeyBindings.AddWaypoint);
    var removeWaypoint = KeyManager.GetButtonDown(ZoopKeyBindings.RemoveWaypoint);

    if ((ZoopRuntime.Controller.IsZoopKeyPressed && primary) || spec)
    {
      ZoopLog.Debug("[Placement] Starting zoop placement.");
      ZoopRuntime.Controller.ToggleZoop(__instance);
    }

    if (addWaypoint && ZoopRuntime.Controller.IsPreviewing)
    {
      ZoopRuntime.Controller.AddWaypoint();
    }

    if (removeWaypoint && ZoopRuntime.Controller.IsPreviewing)
    {
      ZoopRuntime.Controller.RemoveLastWaypoint();
    }

    if (primary && ZoopRuntime.Controller.IsZooping && !ZoopRuntime.Controller.IsZoopKeyPressed)
    {
      ZoopRuntime.Controller.ConfirmZoop(__instance);

      return !ZoopRuntime.Controller.IsZooping;
    }

    if (secondary && ZoopRuntime.Controller.IsZooping)
    {
      ZoopLog.Debug("[Placement] Zoop canceled by right mouse button.");
      ZoopRuntime.Controller.CancelZoop();
    }

    return !ZoopRuntime.Controller.IsZoopKeyPressed;
  }
}
