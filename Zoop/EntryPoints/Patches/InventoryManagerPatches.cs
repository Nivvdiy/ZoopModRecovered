using System;
using System.Collections;
using System.Reflection;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;
using JetBrains.Annotations;
using ZoopMod.Zoop.EntryPoints.Configuration;
using ZoopMod.Zoop.EntryPoints.Input;

namespace ZoopMod.Zoop.EntryPoints.Patches;

[HarmonyPatch(typeof(InventoryManager), "SetMultiConstructorItemPlacement")]
internal static class InventoryManagerSetMultiConstructPatch
{
  [UsedImplicitly]
  public static void Prefix(InventoryManager __instance, MultiConstructor multiConstructorItem)
  {
    if (ZoopRuntime.Controller.IsZooping)
    {
      ZoopMod.Log("detected: " + multiConstructorItem.PrefabHash, ZoopMod.Logs.debug);
      ZoopRuntime.Controller.StartZoop(__instance);
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
      ZoopMod.Log("detected: " + constructorItem.PrefabHash, ZoopMod.Logs.debug);
      ZoopRuntime.Controller.StartZoop(__instance);
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
      ZoopMod.Log("zoop canceled at CancelPlacement", ZoopMod.Logs.debug);
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
      ZoopMod.Log("zoop must start now", ZoopMod.Logs.debug);
      ZoopRuntime.Controller.StartZoop(__instance);
    }

    if (addWaypoint && ZoopRuntime.Controller.IsZooping)
    {
      ZoopRuntime.Controller.AddWaypoint();
    }

    if (removeWaypoint && ZoopRuntime.Controller.IsZooping)
    {
      ZoopRuntime.Controller.RemoveLastWaypoint();
    }

    if (primary && ZoopRuntime.Controller.IsZooping && !ZoopRuntime.Controller.IsZoopKeyPressed)
    {
      if (!ZoopRuntime.Controller.HasError)
      {
        if (!InventoryManager.IsAuthoringMode && InventoryManager.ConstructionCursor.BuildPlacementTime > 0.0)
        {
          var num1 = 1f;

          var inventoryManagerType = typeof(InventoryManager);
          var method = inventoryManagerType.GetMethod("WaitUntilDone",
            BindingFlags.NonPublic | BindingFlags.Instance, null,
            [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
            null);
          if (method != null)
          {
            var actionCoroutine = __instance.StartCoroutine((IEnumerator)method.Invoke(
              __instance,
              [
                new InventoryManager.DelegateEvent(() => ZoopRuntime.Controller.BuildZoop(__instance)),
                InventoryManager.ConstructionCursor.BuildPlacementTime / num1,
                InventoryManager.ConstructionCursor
              ]));
            ZoopRuntime.Controller.SetPendingBuild(__instance, actionCoroutine);
          }
        }
        else
        {
          ZoopRuntime.Controller.BuildZoop(__instance);
        }
      }

      return !ZoopRuntime.Controller.IsZooping;
    }

    if (secondary)
    {
      ZoopMod.Log("zoop canceled by rmb", ZoopMod.Logs.debug);
      ZoopRuntime.Controller.CancelZoop();
    }

    return !ZoopRuntime.Controller.IsZoopKeyPressed;
  }
}
