using Assets.Scripts.UI;
using Assets.Scripts.Util;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UI;
using UnityEngine;
using UnityEngine.UI;
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
						GameObject modSettingsButton = ZoopSettingsTemplates.Instance.AddMenuSettingsButton(buttonGrid, buttonGrid.Find("ButtonGameplay"), "ZoopMod", "SettingsMenuZoop", "icon-zoopmod");
						GameObject modSettingsPage = ZoopSettingsTemplates.Instance.AddMenuSettingsPage(pageGrid, pageGrid.Find("Gameplay"), "ZoopMod");
						
						modSettingsButton.GetComponent<Toggle>().onValueChanged.AddListener(modSettingsPage.SetActive);

						GameObject modSettingsPanel1 = ZoopSettingsTemplates.Instance.AddPageSettingsSection(modSettingsPage.transform, pageGrid.Find("Gameplay/TitleDisplayTools"), "Panel1", "SectionSettingsTitle1");
						GameObject modSettingsPanel1Text = ZoopSettingsTemplates.Instance.AddSectionSettingsText(modSettingsPanel1.transform, pageGrid.Find("Audio/TitleMixer/UserSpeakerModeText"), "Text", "ElementSettingsText");
						GameObject modSettingsPanel1Boolean = ZoopSettingsTemplates.Instance.AddSectionSettingsBoolean(modSettingsPanel1.transform, pageGrid.Find("Gameplay/TitleDisplayTools/ShowFps"), "Boolean", "ElementSettingsBoolean", true);
						//GameObject modSettingsPanel1Int = ZoopSettingsTemplates.Instance.AddSectionSettingsNumber(modSettingsPanel1.transform, pageGrid.Find("Gameplay/TitleDisplayTools/HUDScale"), "Int", "ElementSettingsInt", 10, 50, 0);
						//GameObject modSettingsPanel1Float = ZoopSettingsTemplates.Instance.AddSectionSettingsNumber(modSettingsPanel1.transform, pageGrid.Find("Gameplay/TitleDisplayTools/HUDScale"), "Float", "ElementSettingsFloat", 0.5f, 0, 50);
						GameObject modSettingsPanel1Divider = ZoopSettingsTemplates.Instance.AddSectionSettingsDivider(modSettingsPanel1.transform, pageGrid.Find("Audio/TitleMixer/Divider"));
						GameObject modSettingsPanel1Dropdown = ZoopSettingsTemplates.Instance.AddSectionSettingsStringList(modSettingsPanel1.transform, pageGrid.Find("Video/TitleWindowSettings/Resolution"), "Dropdown", "ElementSettingsDropdown", new List<string>{"Element 1","Element 2","Element 3","Element 4","Element 5","Element 6","Element 7","Element 8","Element 9","Element 10"});
						GameObject modSettingsPanel2 = ZoopSettingsTemplates.Instance.AddPageSettingsSection(modSettingsPage.transform, pageGrid.Find("Gameplay/TitleDisplayTools"), "Panel1", "SectionSettingsTitle2");

						// Calculer la hauteur totale des enfants
						float totalHeight = 0;
						foreach(RectTransform child in modSettingsPanel1.transform) {
							totalHeight += child.rect.height;
						}

						// Ajouter l'espacement entre les éléments
						VerticalLayoutGroup layoutGroup = modSettingsPanel1.GetComponent<VerticalLayoutGroup>();
						if(layoutGroup != null) {
							totalHeight += layoutGroup.spacing * (modSettingsPanel1.transform.childCount - 1);
						}

						// Ajuster la hauteur du panneau
						RectTransform panelRectTransform = modSettingsPanel1.GetComponent<RectTransform>();
						panelRectTransform.sizeDelta = new Vector2(panelRectTransform.sizeDelta.x, totalHeight);



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
