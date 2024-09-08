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
			//MainMenuPage settingsPanel = GameObject.Find("AlertCanvas").GetComponentInChildren<Assets.Scripts.Serialization.Settings>(includeInactive: true).GetComponent<MainMenuPage>();
			MainMenuPage settingsPanel = GameObject.FindObjectsOfType<MainMenuPage>(true).FirstOrDefault(page => page.gameObject.name == "PanelSettings");

			if(settingsPanel != null) {
				Debug.Log((object)("SettingsPanel found -> " + settingsPanel.name));

				ZoopSettingsTemplates.Instance.AddScrollBarToSettingsMenu(ref settingsPanel);
				if(settingsPanel != null) {
					// Find the "ButtonGrid" in the settings panel
					Transform buttonGrid = settingsPanel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ButtonGrid");
					Transform pageGrid = settingsPanel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PageGrid");

					if(buttonGrid != null && pageGrid != null) {
						// Use the MenuTemplate to create a new settings button
						GameObject modSettingsButton = ZoopSettingsTemplates.Instance.AddNewMenuSettingButton("SettingsMenuZoop", buttonGrid, "ZoopMod", "icon-zoopmod");

						//GameObject modPageGrid = ZoopSettingsTemplates.Instance.AddNewMenuSettingsPage("", pageGrid, "", []);

						Debug.Log("ModSettingsToggle added to ButtonGrid.");
					} else {
						Debug.Log("ButtonGrid not found");
					}
				} else {
					Debug.Log("SettingsPanel not found");
				}
			} else {
				Debug.Log("SettingsPanel not found");
			}
		}

	}
}
