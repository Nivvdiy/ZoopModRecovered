using Assets.Scripts.UI;
using HarmonyLib;
using System.Linq;
using UI;
using UnityEngine;
using ZoopMod.Zoop.SettingsMenu;

namespace ZoopMod {

	[HarmonyPatch(typeof(MainMenu), "Start")]
	public static class MainMenuPatch {
		static void Postfix(MainMenu __instance) {
			// Find the "Settings" panel
			MainMenuPage settingsPanel = GameObject.Find("AlertCanvas").GetComponentInChildren<Assets.Scripts.Serialization.Settings>(includeInactive: true).GetComponent<MainMenuPage>();

			if(settingsPanel != null) {
				Debug.Log("SettingsPanel found -> " + settingsPanel.name);

				// Find the "ButtonGrid" in the settings panel
				Transform buttonGrid = settingsPanel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ButtonGrid");

				if(buttonGrid != null) {
					// Use the MenuTemplate to create a new settings button
					GameObject modSettingsButton = ZoopSettingsTemplates.Instance.GetMenuSettingButton("SettingsMenuZoop", buttonGrid, "ZoopMod", "icon-zoopmod");

					// Log the creation process
					LogChildObjects(modSettingsButton.transform);

					Debug.Log("ModSettingsToggle added to ButtonGrid.");
				} else {
					Debug.Log("ButtonGrid not found");
				}
			} else {
				Debug.Log("SettingsPanel not found");
			}
		}

		static void LogChildObjects(Component parent, string parentName = "") {
			foreach(Component child in parent.GetComponentsInChildren<Component>()) {
				if(!parentName.Contains(child.name)) {
					string childName = string.IsNullOrEmpty(parentName) ? child.name : $"{parentName}/{child.name}";
					Debug.Log($"GameObject: {childName}, Type: {child.GetType()}");

					// Recursive call to handle deeper levels
					LogChildObjects(child, childName);
				}
			}
		}

	}
}
