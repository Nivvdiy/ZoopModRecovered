using Assets.Scripts.UI;
using Assets.Scripts.Util;
using ColorBlindUtility.UGUI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace ZoopMod.Zoop.SettingsMenu {
	public class ZoopSettingsTemplates {

		// Singleton instance
		private static ZoopSettingsTemplates _instance;

		// Property to access the singleton instance
		public static ZoopSettingsTemplates Instance {
			get {
				if(_instance == null) {
					_instance = new ZoopSettingsTemplates();
				}
				return _instance;
			}
		}

		public GameObject GetMenuSettingButton(string localizationKey, Transform parent, string buttonName, string iconFileName = null) {
			return null;
		}

		public GameObject GetMenuSettingButtonOld(string localizationKey, Transform parent, string buttonName, string iconFileName = null) {
			// Define the resources for the toggle
			DefaultControls.Resources toggleResources = new DefaultControls.Resources();

			// Copy background and checkmark from the template button
			GameObject templateButton = parent.Find("ButtonGameplay").gameObject;
			toggleResources.background = templateButton.GetComponent<Image>().sprite;

			// Create a new Toggle button using the DefaultControls
			GameObject newButton = DefaultControls.CreateToggle(toggleResources);
			newButton.name = "Button" + buttonName;
			newButton.transform.SetParent(parent, false);

			CopyComponentValues(templateButton.GetComponent<Toggle>(), newButton.GetComponent<Toggle>());

			CopyComponentValues(templateButton.GetComponent<RectTransform>(), newButton.GetComponent<RectTransform>());

			// Add required components from the template to the new button
			AddComponent<CanvasRenderer>(templateButton, newButton);
			AddComponent<Image>(templateButton, newButton);
			AddComponent<Animator>(templateButton, newButton);
			AddComponent<UIAudioComponent>(templateButton, newButton);
			AddComponent<ColorBlindImage>(templateButton, newButton);
			AddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(templateButton, newButton);

			// Remove default children created by DefaultControls
			foreach(Transform child in newButton.transform) {
				Object.Destroy(child.gameObject);
			}

			// Create and add the new children matching the template's structure
			CreateChildWithComponents(newButton.transform, "Toggled", templateButton.transform.Find("Toggled"));
			CreateChildWithComponents(newButton.transform, "ButtonIcon", templateButton.transform.Find("ButtonIcon"));
			CreateChildWithComponents(newButton.transform, "ButtonText", templateButton.transform.Find("ButtonText"));

			Toggle newToggle = newButton.GetComponent<Toggle>();

			newToggle.graphic = newButton.transform.Find("Toggled").GetComponent<Image>();
			newToggle.isOn = false;
			newToggle.image = newButton.GetComponent<Image>();
			newToggle.targetGraphic = newButton.GetComponent<Image>();

			// Modify the text and localization
			LocalizedText localizedText = newButton.GetComponentInChildren<LocalizedText>();
			if(localizedText != null) {
				localizedText.TextMesh = newButton.GetComponentInChildren<TextMeshProUGUI>();
				localizedText.StringKey = localizationKey;
				localizedText.Refresh();
			}

			// Load the icon from a PNG file if provided
			if(!string.IsNullOrEmpty(iconFileName)) {
				Image iconImage = newButton.transform.Find("ButtonIcon")?.GetComponent<Image>();
				if(iconImage != null) {
					Sprite iconSprite = LoadSpriteFromFile(iconFileName, 32, 32);
					if(iconSprite != null) {
						iconImage.overrideSprite = iconSprite;
						iconImage.sprite = iconSprite;
						iconImage.enabled = true;
					}
				} else {
					Debug.LogError("ButtonIcon Image component not found.");
				}
			} else {
				// Disable icon if no valid file is provided
				newButton.transform.Find("ButtonIcon").GetComponent<Image>().enabled = false;
			}

			return newButton;
		}

		private Sprite LoadSpriteFromFile(string fileName, int expectedWidth, int expectedHeight, float pixelsPerUnit = 100.0f) {

			string modPath = Path.GetDirectoryName(Instance.GetType().Assembly.Location);
			string filePath = Path.Combine(modPath, "GameData", "Images", fileName+".png");

			if(!File.Exists(filePath)) {
				Debug.LogError("File does not exist.");
				return null;
			} else if(!Path.GetExtension(filePath).Equals(".png")) {
				Debug.LogError("File is not a PNG");
				return null;
			}

			byte[] fileData = File.ReadAllBytes(filePath);
			Texture2D texture = new Texture2D(2, 2);
			ImageConversion.LoadImage(texture, fileData);

			// Resize to 32x32 if necessary
			if(texture.width != expectedWidth || texture.height != expectedHeight) {
				Rescale(texture, expectedWidth, expectedHeight);
			}
			Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
			sprite.name = fileName;
			return sprite;
		}

		public static void Rescale(Texture2D tex, int newWidth, int newHeight) {
			Texture2D newTex = new Texture2D(newWidth, newHeight);
			float ratioX = (float)tex.width / newWidth;
			float ratioY = (float)tex.height / newHeight;
			float ratio = Mathf.Min(ratioX, ratioY);

			int finalWidth = Mathf.FloorToInt(tex.width / ratio);
			int finalHeight = Mathf.FloorToInt(tex.height / ratio);

			for(int y = 0; y < newHeight; y++) {
				for(int x = 0; x < newWidth; x++) {
					float xSample = x * ratio;
					float ySample = y * ratio;

					newTex.SetPixel(x, y, tex.GetPixelBilinear(xSample / tex.width, ySample / tex.height));
				}
			}
			newTex.Apply();

			tex.Reinitialize(newWidth, newHeight);
			tex.SetPixels(newTex.GetPixels());
			tex.Apply();
		}

		private static void CreateChildWithComponents(Transform parent, string childName, Transform template) {
			GameObject child = new GameObject(childName);
			child.transform.SetParent(parent);

			foreach(Component component in template.GetComponents<Component>()) {
				System.Type componentType = component.GetType();
				Component newComponent = child.AddComponent(componentType);
				CopyComponentValues(component, newComponent);
			}
		}

		private static void AddComponent<T>(GameObject source, GameObject destination) where T : Component {
			T sourceComponent = source.GetComponent<T>();
			if(sourceComponent != null) {
				T destinationComponent = destination.AddComponent<T>();
				CopyComponentValues(sourceComponent, destinationComponent);
			}
		}

		private static void CopyComponentValues(Component original, Component copy) {
			var type = original.GetType();

			// Copy fields
			var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			foreach(var field in fields) {
				if(!field.Name.Equals("name") && !field.Name.Contains("parent") && !field.Name.Equals("onValueChanged")) {
					field.SetValue(copy, field.GetValue(original));
				}
			}

			// Copy properties
			var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			foreach(var property in properties) {
				if(property.CanWrite && property.GetMethod != null && property.SetMethod != null && !property.Name.Contains("parent") && !property.Name.Equals("name") && !property.Name.Equals("onValueChanged")) {
					try {
						property.SetValue(copy, property.GetValue(original));
					} catch {
						Debug.LogWarning($"Failed to copy property {property.Name} from {original} to {copy}");
					}
				}
			}
		}

		public MainMenuPage TransformPanelSettingsToWorkshopStyle(MainMenuPage panelSettings) {
			MainMenuPage panelWorkshopMods = GameObject.FindObjectsOfType<MainMenuPage>(true).FirstOrDefault(page => page.gameObject.name == "PanelWorkshopMods");
			// Localiser les GameObjects sources et cibles
			Transform sourceButtonList = panelWorkshopMods.transform.Find("ModList");
			Transform sourceWindow = sourceButtonList?.Find("Window");
			Transform sourceScrollbar = sourceButtonList?.Find("Scrollbar");
			Transform sourceDynamicGrid = sourceWindow?.Find("DynamicGrid");

			if(sourceButtonList == null || sourceWindow == null || sourceScrollbar == null || sourceDynamicGrid == null) {
				Debug.LogError("Source GameObjects not found in PanelWorkshopMods.");
				return null;
			}

			// Créer les GameObjects cibles sous PanelSettings
			Transform panelServerWindow = panelSettings.transform.Find("PanelServerWindow");

			if(panelServerWindow == null) {
				Debug.LogError("PanelServerWindow not found in PanelSettings.");
				return null;
			}

			Transform oldButtonGrid = panelServerWindow.Find("ButtonGrid");
			if(oldButtonGrid == null) {
				Debug.LogError("ButtonGrid not found in PanelSettings.");
				return null;
			}

			// Créer le nouveau ButtonList, Window et Scrollbar
			GameObject buttonListGO = new GameObject("ButtonList");
			buttonListGO.transform.SetParent(panelServerWindow, false);

			GameObject windowGO = new GameObject("Window");
			windowGO.transform.SetParent(buttonListGO.transform, false);

			GameObject scrollbarGO = new GameObject("Scrollbar");
			scrollbarGO.transform.SetParent(buttonListGO.transform, false);

			// Copier les composants
			RectTransform rectButtonList = CopyAndAddComponent<RectTransform>(sourceButtonList, buttonListGO);
			CopyAndAddComponent<CanvasRenderer>(oldButtonGrid, buttonListGO);
			CopyAndAddComponent<Image>(oldButtonGrid, buttonListGO);
			CopyAndAddComponent<ColorBlindImage>(oldButtonGrid, buttonListGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(oldButtonGrid, buttonListGO);
			CopyAndAddComponent<ScrollRect>(sourceButtonList, buttonListGO);

			RectTransform rectWindow = CopyAndAddComponent<RectTransform>(sourceWindow, windowGO);
			CopyAndAddComponent<CanvasRenderer>(sourceWindow, windowGO);
			CopyAndAddComponent<Image>(sourceWindow, windowGO);
			CopyAndAddComponent<UnityEngine.UI.Mask>(sourceWindow, windowGO);
			CopyAndAddComponent<RectMask2D>(sourceWindow, windowGO);
			CopyAndAddComponent<ColorBlindImage>(sourceWindow, windowGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceWindow, windowGO);

			// Créer le "Sliding Area" pour la scrollbar
			GameObject slidingAreaGO = new GameObject("Sliding Area");
			slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);

			// Copier les composants du "Sliding Area"
			CopyAndAddComponent<RectTransform>(sourceScrollbar.Find("Sliding Area"), slidingAreaGO).anchoredPosition = new Vector2(0, 0);

			// Créer le "Handle" pour le "Sliding Area"
			GameObject handleGO = new GameObject("Handle");
			handleGO.transform.SetParent(slidingAreaGO.transform, false);

			// Copier les composants du "Handle"
			Transform sourceHandle = sourceScrollbar.Find("Sliding Area/Handle");
			if(sourceHandle != null) {
				CopyAndAddComponent<RectTransform>(sourceHandle, handleGO).anchoredPosition = new Vector2(0, 0);
				CopyAndAddComponent<CanvasRenderer>(sourceHandle, handleGO);
				CopyAndAddComponent<Image>(sourceHandle, handleGO);
				CopyAndAddComponent<ColorBlindImage>(sourceHandle, handleGO);
				CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceHandle, handleGO);
			}

			// Copier les composants de la scrollbar
			RectTransform rectScrollbar = CopyAndAddComponent<RectTransform>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<CanvasRenderer>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<Image>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<Scrollbar>(sourceScrollbar, scrollbarGO).handleRect = handleGO.GetComponent<RectTransform>();
			CopyAndAddComponent<ColorBlindImage>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceScrollbar, scrollbarGO);

			// Créer le nouveau ButtonGrid basé sur DynamicGrid
			GameObject newButtonGrid = new GameObject("ButtonGrid");
			newButtonGrid.transform.SetParent(windowGO.transform, false);

			// Copier les composants de DynamicGrid vers le nouveau ButtonGrid
			CopyAndAddComponent<RectTransform>(sourceDynamicGrid, newButtonGrid).anchoredPosition = new Vector2(0, 0);
			CopyAndAddComponent<GridLayoutGroup>(sourceDynamicGrid, newButtonGrid);
			CopyAndAddComponent<ContentSizeFitter>(sourceDynamicGrid, newButtonGrid);

			// Récupérer et appliquer le ToggleGroup de l'ancien ButtonGrid
			ToggleGroup oldToggleGroup = oldButtonGrid.GetComponent<ToggleGroup>();
			ToggleGroup newToggleGroup = null;
			if(oldToggleGroup != null) {
				newToggleGroup = newButtonGrid.AddComponent<ToggleGroup>();
				CopyComponentValues(oldToggleGroup, newToggleGroup);
			}

			// Déplacer tous les enfants de l'ancien ButtonGrid vers le nouveau ButtonGrid
			List<Transform> children = new List<Transform>();
			Rect buttonSize = Rect.zero;
			foreach(Transform child in oldButtonGrid) {
				children.Add(child);
				if(buttonSize == Rect.zero) {
					buttonSize = child.GetComponent<RectTransform>().rect;
				}
			}

			foreach(Transform child in children) {
				child.SetParent(newButtonGrid.transform, false);
				Toggle toggle = child.GetComponent<Toggle>();
				if(toggle != null)
					toggle.group = newToggleGroup ?? null;
			}

			// Configurer le ScrollRect pour le nouveau ButtonGrid
			ScrollRect scrollRect = buttonListGO.GetComponent<ScrollRect>();
			scrollRect.content = newButtonGrid.GetComponent<RectTransform>();
			scrollRect.viewport = windowGO.GetComponent<RectTransform>();
			scrollRect.verticalScrollbar = scrollbarGO.GetComponent<Scrollbar>();
			scrollRect.horizontal = false;
			scrollRect.vertical = true;

			Vector2 oldOffsetMax = oldButtonGrid.GetComponent<RectTransform>().offsetMax;
			Vector2 oldOffsetMin = oldButtonGrid.GetComponent<RectTransform>().offsetMin;

			rectWindow.anchorMax = Vector2.up;
			rectWindow.offsetMax = new Vector2(buttonSize.width, 0);
			rectWindow.offsetMin = new Vector2(0, 0);

			rectScrollbar.anchorMin = Vector2.right;
			rectScrollbar.offsetMax = new Vector2(0, rectWindow.offsetMax.y);
			rectScrollbar.offsetMin = new Vector2(-10, rectWindow.offsetMin.y);

			rectButtonList.anchorMax = Vector2.up;
			rectButtonList.offsetMax = new Vector2(oldOffsetMin.x + buttonSize.width + rectScrollbar.rect.width + 5, oldOffsetMax.y);
			rectButtonList.offsetMin = new Vector2(oldOffsetMin.x, sourceButtonList.GetComponent<RectTransform>().offsetMin.y);

			newButtonGrid.GetComponent<GridLayoutGroup>().cellSize = buttonSize.size;

			// Supprimer l'ancien ButtonGrid
			Object.DestroyImmediate(oldButtonGrid.gameObject);

			/*AddOutlineToPanel(windowGO.gameObject, Color.cyan);
			AddOutlineToPanel(buttonListGO.gameObject, Color.green);
			AddOutlineToPanel(sourceButtonList.gameObject, Color.green);
			AddOutlineToPanel(sourceWindow.gameObject, Color.cyan);
			AddOutlineToPanel(scrollbarGO.gameObject, Color.yellow);
			AddOutlineToPanel(sourceScrollbar.gameObject, Color.yellow);*/

			return panelSettings;
		}
		public void AddOutlineToPanel(GameObject panel, Color color, float thickness = 2f) {
			// Créer les quatre images pour simuler l'outline
			CreateOutlineEdge(panel, "TopOutline", color, thickness, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness));
			CreateOutlineEdge(panel, "BottomOutline", color, thickness, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness));
			CreateOutlineEdge(panel, "LeftOutline", color, thickness, new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0));
			CreateOutlineEdge(panel, "RightOutline", color, thickness, new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0));
		}

		private void CreateOutlineEdge(GameObject parent, string name, Color color, float thickness, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta) {
			// Créer un nouvel objet pour l'outline edge
			GameObject outlineEdge = new GameObject(name);
			outlineEdge.transform.SetParent(parent.transform, false);

			// Ajouter un composant Image
			Image image = outlineEdge.AddComponent<Image>();
			image.color = color;

			// Ajuster le RectTransform pour correspondre à la bordure spécifique
			RectTransform rectTransform = outlineEdge.GetComponent<RectTransform>();
			rectTransform.anchorMin = anchorMin;
			rectTransform.anchorMax = anchorMax;
			rectTransform.sizeDelta = sizeDelta;

			Color transparent = color;
			transparent.SetAlpha(0.3f);
			Outline outline = parent.AddComponent<Outline>();
			outline.useGraphicAlpha = true;
			outline.effectColor = transparent;
		}

		private T CopyAndAddComponent<T>(Transform source, GameObject destination) where T : Component {
			T sourceComponent = source.GetComponent<T>();
			if(sourceComponent != null) {
				T destinationComponent = destination.AddComponent<T>();
				CopyComponentValues(sourceComponent, destinationComponent);
				return destination.GetComponent<T>();
			}
			return null;
		}

	}
}