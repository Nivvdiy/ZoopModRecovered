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
        //Debug.Log("zoop canceled at CancelPlacement");
        ZoopUtility.CancelZoop();
        ZoopUtility.isZoopKeyPressed = false;
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
        int structureCount = Math.Max(ZoopUtility.structures.Count, 1);
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
    //public static bool CFree = false;
    [UsedImplicitly]
    public static bool Prefix(InventoryManager __instance)
    {
      ZoopUtility.isZoopKeyPressed = KeyManager.GetButton(ZoopMod.ZoopHold);
      bool secondary = KeyManager.GetMouseDown("Secondary");
      bool primary = KeyManager.GetMouseDown("Primary");
      bool spec = KeyManager.GetButtonDown(ZoopMod.ZoopSwitch);
      bool addWaypoint = KeyManager.GetButtonDown(ZoopMod.ZoopAddWaypoint);
      bool removeWaypoint = KeyManager.GetButtonDown(ZoopMod.ZoopRemoveWaypoint);
      //bool place = KeyManager.GetButton(KeyMap.PrecisionPlace);

      if (ZoopUtility.isZoopKeyPressed && primary || spec)
      {
        // Debug.Log("zoop must start now");
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


      if (primary && ZoopUtility.isZooping && !ZoopUtility.isZoopKeyPressed)
      {
        if (!ZoopUtility.HasError)
        {
          //NotAuthoringMode.Completion = true; //try not let original InventoryManager.UsePrimaryComplete override completion for Authoring Tool

          //CHANGE tried to evade authoring mode check, as zero placement time is it
          if (!InventoryManager.IsAuthoringMode && InventoryManager.ConstructionCursor.BuildPlacementTime > 0.0)
          {
            float num1 = 1f;
            //if (InventoryManager.ParentHuman.Suit == null)//((UnityEngine.Object)InventoryManager.ParentHuman.Suit == (UnityEngine.Object)null) //did make errors at stable update 24.04.2024
            //    num1 += 0.2f; //whyyy make it longer in suit there...
            //float num2 = Mathf.Clamp(num1, 0.2f, 5f); //nosuit make number bigger

            Type inventoryManagerType = typeof(InventoryManager);
            MethodInfo method = inventoryManagerType.GetMethod("WaitUntilDone",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure)],
                null);
            if (method != null)
              ZoopUtility.ActionCoroutine = __instance.StartCoroutine((IEnumerator)method.Invoke(
                  __instance,
                  [
                      //new InventoryManager.DelegateEvent(() => UniTask.Run(async () => await ZoopUtility.BuildZoopAsync(__instance))),
                      new InventoryManager.DelegateEvent(() => ZoopUtility.BuildZoop(__instance)),
                                        InventoryManager.ConstructionCursor.BuildPlacementTime /
                                        num1, //num2, //bigger number makes it spend less time
										InventoryManager.ConstructionCursor
                  ])
              );
          }
          else
            ZoopUtility.BuildZoop(__instance);
          //UniTask.Run(async () => await ZoopUtility.BuildZoopAsync(__instance)); //not finishing line properly stop but don't know why with waypoints addition feature
        }

        return !ZoopUtility.isZooping;
      }

      if (secondary)// || drop)
      {
        //Debug.Log("zoop canceled by rmb");
        ZoopUtility.CancelZoop();
      }

      return !ZoopUtility.isZoopKeyPressed;
    }
  }

  [HarmonyPatch(typeof(ConstructionPanel), "SelectUp")]
  public class ConstructionPanelSelectUp
  {
    [UsedImplicitly]
    public static bool Prefix()
    {
      return !(ZoopUtility.isZoopKeyPressed);
    }
  }

  [HarmonyPatch(typeof(ConstructionPanel), "SelectDown")]
  public class ConstructionPanelSelectDown
  {
    [UsedImplicitly]
    public static bool Prefix()
    {
      return !(ZoopUtility.isZoopKeyPressed);
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
            ZoopUtility.lineColor.SetAlpha(InventoryManager.Instance.CursorAlphaInteractable);
      }
    }
  }
}
