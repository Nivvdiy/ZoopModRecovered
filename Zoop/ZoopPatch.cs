using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;
using Assets.Scripts.Util;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Reflection;

namespace ZoopMod {
	[HarmonyPatch(typeof(InventoryManager), "SetMultiConstructorItemPlacement")]
	public class InventoryManagerSetMultiContstruct {
		[UsedImplicitly]
		public static void Prefix(InventoryManager __instance, MultiConstructor multiConstructorItem) {
			if(ZoopUtility.isZooping) {
				//ConsoleWindow.Print("detected: " + multiConstructorItem.PrefabHash);
				ZoopUtility.StartZoop(__instance);
			}
		}
	}

	[HarmonyPatch(typeof(InventoryManager), "SetConstructorItemPlacement")]
	public class InventoryManagerSetContstruct {
		[UsedImplicitly]
		public static void Prefix(InventoryManager __instance, Constructor constructorItem) {
			if(ZoopUtility.isZooping) {
				//ConsoleWindow.Print("detected: " + constructorItem.PrefabHash);
				ZoopUtility.StartZoop(__instance);
			}
		}
	}

	[HarmonyPatch(typeof(InventoryManager), "CancelPlacement")]
	public class InventoryManagerCancelPlacement {
		[UsedImplicitly]
		public static void Prefix(InventoryManager __instance) {
			if(ZoopUtility.isZooping) {
				//Debug.Log("zoop canceled at CancelPlacement");
				ZoopUtility.CancelZoop();
				ZoopUtility.isZoopKeyPressed = false;
			}
		}
	}

	[HarmonyPatch(typeof(InventoryManager), "UpdatePlacement", new Type[] { typeof(Constructor) })]
	public class InventoryManagerUpdatePlacementConstructor {
		[UsedImplicitly]
		public static bool Prefix(InventoryManager __instance) {
			return !ZoopUtility.isZooping; //false prevents placing down item //NICE CHECK
		}
	}

	[HarmonyPatch(typeof(InventoryManager), "UpdatePlacement", new Type[] { typeof(Structure) })]
	public class InventoryManagerUpdatePlacementStructure {
		[UsedImplicitly]
		public static bool Prefix(InventoryManager __instance) {
			return !ZoopUtility.isZooping; //false prevents placing down item
		}
	}

	[HarmonyPatch(typeof(InventoryManager), "WaitUntilDone",
		new Type[] { typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure) })]
	public class InventoryManagerWaitUntilDone0 {
		[UsedImplicitly]
		public static void Prefix(InventoryManager __instance, InventoryManager.DelegateEvent onFinished,
			ref float timeToWait,
			Structure structure) {
			if(!InventoryManager.IsAuthoringMode) {
				if(ZoopUtility.structures.Count <= 0) {
					timeToWait = Math.Min(timeToWait * 1, timeToWait * 10);
				} else {
					timeToWait = Math.Min(timeToWait * ZoopUtility.structures.Count, timeToWait * 10); //PROBLEM same time of placement for single pieces after zooping
				}
			} else timeToWait = 0f; //try to make it instant for creative tool
		}
	}

	[HarmonyPatch(typeof(InventoryManager), "PlacementMode")]
	public class InventoryManagerPlacementMode {
		//public static bool CFree = false;
		[UsedImplicitly]
		public static bool Prefix(InventoryManager __instance) {
			if(GameManager.RunSimulation) //not let it work in multiplayer client, as it bring errors there
			{

				bool scrollUp = __instance.newScrollData > 0f;
				bool scrollDown = __instance.newScrollData < 0f;
				ZoopUtility.isZoopKeyPressed = KeyManager.GetButton(ZoopMod.ZoopHold);
				bool secondary = KeyManager.GetMouseDown("Secondary");
				bool primary = KeyManager.GetMouseDown("Primary");
				bool spec = KeyManager.GetButtonDown(ZoopMod.ZoopSwitch);
				bool addWaypoint = KeyManager.GetButtonDown(ZoopMod.ZoopAddWaypoint);
				bool removeWaypoint = KeyManager.GetButtonDown(ZoopMod.ZoopRemoveWaypoint);
				//bool place = KeyManager.GetButton(KeyMap.PrecisionPlace);

				if(ZoopUtility.isZoopKeyPressed && primary || spec) {
					// Debug.Log("zoop must start now");
					ZoopUtility.StartZoop(__instance);
				}

				if(addWaypoint && ZoopUtility.isZooping) {
					ZoopUtility.AddWaypoint();
				}

				if(removeWaypoint && ZoopUtility.isZooping) {
					ZoopUtility.RemoveLastWaypoint();
				}


				if(primary && ZoopUtility.isZooping && !ZoopUtility.isZoopKeyPressed) {
					if(!ZoopUtility.HasError) {
						//NotAuthoringMode.Completion = true; //try not let original InventoryManager.UsePrimaryComplete override completion for Authoring Tool

						//CHANGE tried to evade authoring mode check, as zero placement time is it
						if(!InventoryManager.IsAuthoringMode && (double)InventoryManager.ConstructionCursor.BuildPlacementTime > 0.0) {
							float num1 = 1f;
							//if (InventoryManager.ParentHuman.Suit == null)//((UnityEngine.Object)InventoryManager.ParentHuman.Suit == (UnityEngine.Object)null) //did make errors at stable update 24.04.2024
							//    num1 += 0.2f; //whyyy make it longer in suit there...
							//float num2 = Mathf.Clamp(num1, 0.2f, 5f); //nosuit make number bigger

							Type InventoryManagerType = typeof(InventoryManager);
							var method = InventoryManagerType.GetMethod("WaitUntilDone",
								BindingFlags.NonPublic | BindingFlags.Instance, null,
								new Type[] { typeof(InventoryManager.DelegateEvent), typeof(float), typeof(Structure) },
								null);
							ZoopUtility.ActionCoroutine = __instance.StartCoroutine((IEnumerator)method.Invoke(__instance,
								new Object[]
								{
								//new InventoryManager.DelegateEvent(() => UniTask.Run(async () => await ZoopUtility.BuildZoopAsync(__instance))),
								new InventoryManager.DelegateEvent(() => ZoopUtility.BuildZoop(__instance)),
								InventoryManager.ConstructionCursor.BuildPlacementTime / num1,//num2, //bigger number makes it spend less time
                                InventoryManager.ConstructionCursor
								})
							);
						} else
							ZoopUtility.BuildZoop(__instance);
						//UniTask.Run(async () => await ZoopUtility.BuildZoopAsync(__instance)); //not finishing line properly stop but don't know why with waypoints addition feature
					}

					return !ZoopUtility.isZooping;
				}

				if(secondary)// || drop)
				{
					//Debug.Log("zoop canceled by rmb");
					ZoopUtility.CancelZoop();
				}

				return !ZoopUtility.isZoopKeyPressed;
			} else return true; //let normal building work in multiplayer client too.
		}
	}

	[HarmonyPatch(typeof(ConstructionPanel), "SelectUp")]
	public class ConstructionPanelSelectUp {
		[UsedImplicitly]
		public static bool Prefix() {
			return !(ZoopUtility.isZoopKeyPressed);
		}
	}

	[HarmonyPatch(typeof(ConstructionPanel), "SelectDown")]
	public class ConstructionPanelSelectDown {
		[UsedImplicitly]
		public static bool Prefix() {
			return !(ZoopUtility.isZoopKeyPressed);
		}
	}

	[HarmonyPatch(typeof(CursorManager), "SetSelectionColor")]
	public class CursorManagerSetSelectionColor {
		[UsedImplicitly]
		public static void Postfix() {
			if(ZoopUtility.isZooping) {
				CursorManager.CursorSelectionRenderer.material.color =
					ZoopUtility.lineColor.SetAlpha(InventoryManager.Instance.CursorAlphaInteractable);
			}
		}
	}
}
