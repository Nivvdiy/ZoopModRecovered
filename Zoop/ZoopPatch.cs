using System;
using System.Collections;
using System.Reflection;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;
using Assets.Scripts.Util;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace ZoopMod.Zoop
{
  [HarmonyPatch(typeof(InventoryManager), "SetMultiConstructorItemPlacement")]
  public class InventoryManagerSetMultiConstruct
  {
    [UsedImplicitly]
    public static void Prefix(InventoryManager __instance, MultiConstructor multiConstructorItem)
    {
      if (ZoopUtility.isZooping)
      {
        //ConsoleWindow.Print("detected: " + multiConstructorItem.PrefabHash);
        ZoopMod.Log("detected: " + multiConstructorItem.PrefabHash, ZoopMod.Logs.debug);
        ZoopUtility.StartZoop(__instance);
      }
    }
  }

  [HarmonyPatch(typeof(InventoryManager), "SetConstructorItemPlacement")]
  public class InventoryManagerSetConstruct
  {
    [UsedImplicitly]
    public static void Prefix(InventoryManager __instance, Constructor constructorItem)
    {
      if (ZoopUtility.isZooping)
      {
        //ConsoleWindow.Print("detected: " + constructorItem.PrefabHash);
        ZoopMod.Log("detected: " + constructorItem.PrefabHash, ZoopMod.Logs.debug);
        ZoopUtility.StartZoop(__instance);
      }
    }
  }

  [HarmonyPatch(typeof(InventoryManager), "CancelPlacement")]
  public class InventoryManagerCancelPlacement
  {
    [UsedImplicitly]
    public static void Prefix(InventoryManager __instance)
    {
      if (ZoopUtility.isZooping)
      {
        ZoopMod.Log("zoop canceled at CancelPlacement", ZoopMod.Logs.debug);
        ZoopUtility.CancelZoop();
        ZoopUtility.IsZoopKeyPressed = false;
      }
    }
  }

  [HarmonyPatch(typeof(InventoryManager), "UpdatePlacement", [typeof(Constructor)])]
  public class InventoryManagerUpdatePlacementConstructor
  {
    [UsedImplicitly]
    public static bool Prefix(InventoryManager __instance)
    {
      return !ZoopUtility.isZooping || ZoopUtility.AllowPlacementUpdate; //false prevents placing down item //NICE CHECK
    }
  }

  [HarmonyPatch(typeof(InventoryManager), "UpdatePlacement", [typeof(Structure)])]
  public class InventoryManagerUpdatePlacementStructure
  {
    [UsedImplicitly]
    public static bool Prefix(InventoryManager __instance)
    {
      return !ZoopUtility.isZooping || ZoopUtility.AllowPlacementUpdate; //false prevents placing down item
    }
  }

  [HarmonyPatch(typeof(InventoryManager), "WaitUntilDone",
      [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)])]
  public class InventoryManagerWaitUntilDone0
  {
    [UsedImplicitly]
    public static void Prefix(InventoryManager __instance, InventoryManager.DelegateEvent onFinished, ref float timeToWait, Structure structure)
    {
      if (!ZoopUtility.isZooping)
      {
        return;
      }

      if (!InventoryManager.IsAuthoringMode)
      {
        int structureCount = Math.Max(ZoopUtility.Structures.Count, 1);
        // TODO Consider putting this into config
        const int timeToWaitMultiplier = 5;
        timeToWait *= Math.Min(structureCount, timeToWaitMultiplier); //switch(ZoopConfig.GetDifficulty())
      }
      else timeToWait = 0f; //try to make it instant for creative tool
    }
  }

  [HarmonyPatch(typeof(InventoryManager), "PlacementMode")]
  public class InventoryManagerPlacementMode
  {
    [UsedImplicitly]
    public static bool Prefix(InventoryManager __instance)
    {
      ZoopUtility.IsZoopKeyPressed = KeyManager.GetButton(ZoopMod.ZoopHold);
      bool secondary = KeyManager.GetMouseDown("Secondary");
      bool primary = KeyManager.GetMouseDown("Primary");
      bool spec = KeyManager.GetButtonDown(ZoopMod.ZoopSwitch);
      bool addWaypoint = KeyManager.GetButtonDown(ZoopMod.ZoopAddWaypoint);
      bool removeWaypoint = KeyManager.GetButtonDown(ZoopMod.ZoopRemoveWaypoint);

      if ((ZoopUtility.IsZoopKeyPressed && primary) || spec)
      {
        ZoopMod.Log("zoop must start now", ZoopMod.Logs.debug);
        ZoopUtility.StartZoop(__instance);
      }

      if (addWaypoint && ZoopUtility.isZooping)
      {
        ZoopUtility.AddWaypoint();
      }

      if (removeWaypoint && ZoopUtility.isZooping)
      {
        ZoopUtility.RemoveLastWaypoint();
      }


      if (primary && ZoopUtility.isZooping && !ZoopUtility.IsZoopKeyPressed)
      {
        if (!ZoopUtility.HasError)
        {
          if (!InventoryManager.IsAuthoringMode && InventoryManager.ConstructionCursor.BuildPlacementTime > 0.0)
          {
            float num1 = 1f;

            Type inventoryManagerType = typeof(InventoryManager);
            MethodInfo method = inventoryManagerType.GetMethod("WaitUntilDone",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
                null);
            if (method != null)
            {
              Coroutine actionCoroutine = __instance.StartCoroutine((IEnumerator)method.Invoke(
                  __instance,
                  [
                      new InventoryManager.DelegateEvent(() => ZoopUtility.BuildZoop(__instance)),
                                        InventoryManager.ConstructionCursor.BuildPlacementTime /
                                        num1,
                    InventoryManager.ConstructionCursor
                  ])
              );
              ZoopUtility.SetPendingBuild(__instance, actionCoroutine);
            }
          }
          else
            ZoopUtility.BuildZoop(__instance);
        }

        return !ZoopUtility.isZooping;
      }

      if (secondary)
      {
        ZoopMod.Log("zoop canceled by rmb", ZoopMod.Logs.debug);
        ZoopUtility.CancelZoop();
      }

      return !ZoopUtility.IsZoopKeyPressed;
    }
  }

  [HarmonyPatch(typeof(ConstructionPanel), "SelectUp")]
  public class ConstructionPanelSelectUp
  {
    [UsedImplicitly]
    public static bool Prefix()
    {
      return !(ZoopUtility.IsZoopKeyPressed);
    }
  }

  [HarmonyPatch(typeof(ConstructionPanel), "SelectDown")]
  public class ConstructionPanelSelectDown
  {
    [UsedImplicitly]
    public static bool Prefix()
    {
      return !(ZoopUtility.IsZoopKeyPressed);
    }
  }

  [HarmonyPatch(typeof(CursorManager), "SetSelectionColor")]
  public class CursorManagerSetSelectionColor
  {
    [UsedImplicitly]
    public static void Postfix()
    {
      if (ZoopUtility.isZooping)
      {
        CursorManager.CursorSelectionRenderer.material.color =
            ZoopUtility.LineColor.SetAlpha(InventoryManager.Instance.CursorAlphaInteractable);
      }
    }
  }
}
