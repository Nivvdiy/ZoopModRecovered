using Assets.Scripts.UI;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ZoopMod.Zoop {
	class ZoopKeys {

		/* Custom shortcut key binding injection is done after KeyManager.SetupKeyBindings() method is 
         * called; this way, we can get our custom new bindings saved/load by the game during the 
         * controls initialisation without needing any extra file access.
         */
		[HarmonyPatch(typeof(KeyManager), "SetupKeyBindings")]
		class ShortcutInjectBindingGroup {
			public static void Postfix() {
				// We need to add a custom control group for the keys to be attached to, and create 
				// the Lookout reference.
				Debug.Log("Adding custom Shortcuts group");
				ControlsGroup controlsGroup1 = new ControlsGroup("Zoop");
				KeyManager.AddGroupLookup(controlsGroup1);

				// We will add the custom keys with default values to the KeyItem list using our new
				// created control group, however this method -due to accesibility of the class method-
				// will change the current ControlGroup name.



				ShortcutInjectBindingGroup.AddKey("Zoop Hold", KeyCode.LeftShift, controlsGroup1, false);
				ShortcutInjectBindingGroup.AddKey("Zoop Switch", KeyCode.Z, controlsGroup1, false);
				ShortcutInjectBindingGroup.AddKey("Zoop Add Waypoint", KeyCode.Mouse2, controlsGroup1, false);
				ShortcutInjectBindingGroup.AddKey("Zoop Remove Last Waypoint", KeyCode.V, controlsGroup1, false);


				// We need to restore the name of the control group back to its correct string
				//controlsGroup1.Name = "ShortCuts";

				ControlsAssignment.RefreshState();
			}

			/* Custom method to add keys to a ControlGroup. We 'hijack' the control group lookup function 
             * that will also save the name of they key in the list for us
             */
			private static void AddKey
			(string  assignmentName, KeyCode keyCode, ControlsGroup controlsGroup, bool hidden = false) {
				// This is just because of the accessibility to change the assigned name, we use 
				// the control group for that.
				Dictionary<string, ControlsGroup> controlsLookupList = Traverse.Create(typeof(KeyManager)).Field("_controlsGroupLookup").GetValue() as Dictionary<string, ControlsGroup>; //read static field
				if(controlsLookupList != null) {
					controlsLookupList[assignmentName] = controlsGroup;
					Traverse.Create(typeof(KeyManager)).Field("_controlsGroupLookup").SetValue(controlsLookupList); //for static fields
				}

				// Now Create the key, add its looup string name, and save it in the allkeys list, to ensure
				// is being saved/load by the game config initialisation function.
				KeyItem keyItem = new KeyItem(assignmentName, keyCode, hidden);
				KeyManager.KeyItemLookup[assignmentName] = keyItem;
				KeyManager.AllKeys.Add(keyItem);
			}
		}
	}
}