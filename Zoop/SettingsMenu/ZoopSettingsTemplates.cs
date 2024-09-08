using Assets.Scripts.UI;
using Assets.Scripts.Util;
using ColorBlindUtility.UGUI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace ZoopMod.Zoop.SettingsMenu {
	public class ZoopSettingsTemplates {

		private static ZoopSettingsTemplates _instance;
		private static readonly object instanceLock = new object();
		public static ZoopSettingsTemplates Instance {
			get {
				if(_instance == null) {
					lock(instanceLock) {
						if(_instance == null) {
							_instance = new ZoopSettingsTemplates();
						}
					}
				}
				return _instance;
			}
		}

		private ZoopSettingsTemplates() {}

		#region Menu Button Creation

		public GameObject AddNewMenuSettingButton(string localizationKey, Transform parent, string buttonName, string iconFileName = null) {

			DefaultControls.Resources toggleResources = new DefaultControls.Resources();

			Transform sourceButton = parent.Find("ButtonGameplay");
			if(sourceButton == null) {
				Debug.LogError("ButtonGameplay not found in PanelSettings");
			}
			toggleResources.background = sourceButton.GetComponent<Image>().sprite;

			GameObject newButtonGo = DefaultControls.CreateToggle(toggleResources);
			foreach(Transform child in newButtonGo.transform) {
				Object.Destroy(child.gameObject);
			}

			newButtonGo.name = "Button" + buttonName;
			newButtonGo.transform.SetParent(parent, false);
			Toggle newButtonToggle = CopyComponentValues<Toggle>(sourceButton, newButtonGo);
			CopyComponentValues<RectTransform>(sourceButton, newButtonGo);
			CopyAndAddComponent<CanvasRenderer>(sourceButton, newButtonGo);
			Image newButtonImg = CopyAndAddComponent<Image>(sourceButton, newButtonGo);
			CopyAndAddComponent<Animator>(sourceButton, newButtonGo);
			CopyAndAddComponent<UIAudioComponent>(sourceButton, newButtonGo);
			CopyAndAddComponent<ColorBlindImage>(sourceButton, newButtonGo);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceButton, newButtonGo);

			Transform sourceToggled = sourceButton.Find("Toggled");
			GameObject toggledGO = new GameObject("Toggled");
			toggledGO.transform.SetParent(newButtonGo.transform, false);
			CopyAndAddComponent<RectTransform>(sourceToggled, toggledGO);
			Image toggledImg = CopyAndAddComponent<Image>(sourceToggled, toggledGO);
			CopyAndAddComponent<ColorBlindImage>(sourceToggled, toggledGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceToggled, toggledGO);

			Transform sourceButtonIcon = sourceButton.Find("ButtonIcon");
			GameObject buttonIconGO = new GameObject("ButtonIcon");
			buttonIconGO.transform.SetParent(newButtonGo.transform, false);
			CopyAndAddComponent<RectTransform>(sourceButtonIcon, buttonIconGO);
			Image buttonIconImg = CopyAndAddComponent<Image>(sourceButtonIcon, buttonIconGO);
			CopyAndAddComponent<ColorBlindImage>(sourceButtonIcon, buttonIconGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceButtonIcon, buttonIconGO);

			Transform sourceButtonText = sourceButton.Find("ButtonText");
			GameObject buttonTextGO = new GameObject("ButtonText");
			buttonTextGO.transform.SetParent(newButtonGo.transform, false);
			CopyAndAddComponent<RectTransform>(sourceButtonText, buttonTextGO);
			TextMeshProUGUI buttonTextTMP = CopyAndAddComponent<TextMeshProUGUI>(sourceButtonText, buttonTextGO);
			LocalizedText buttonTextLocalizedText = CopyAndAddComponent<LocalizedText>(sourceButtonText, buttonTextGO);

			newButtonToggle.graphic = toggledImg;
			newButtonToggle.isOn = false;
			newButtonToggle.image = newButtonImg;
			newButtonToggle.targetGraphic = newButtonImg;

			// Modify the text and localization
			if(buttonTextLocalizedText != null) {
				buttonTextLocalizedText.TextMesh = buttonTextTMP;
				buttonTextLocalizedText.StringKey = localizationKey;
				buttonTextLocalizedText.Refresh();
			}

			// Load the icon from a PNG file if provided
			if(!string.IsNullOrEmpty(iconFileName)) {
				if(buttonIconImg != null) {
					Sprite iconSprite = LoadSpriteFromFile(iconFileName, 32, 32);
					if(iconSprite != null) {
						buttonIconImg.overrideSprite = iconSprite;
						buttonIconImg.sprite = iconSprite;
						buttonIconImg.enabled = true;
					}
				} else {
					Debug.LogError("ButtonIcon Image component not found.");
				}
			} else {
				// Disable icon if no valid file is provided
				buttonIconImg.enabled = false;
			}

			return newButtonGo;
		}

		#endregion

		#region Panel Transformation
		public void AddScrollBarToSettingsMenu(ref MainMenuPage panelSettings) {

			MainMenuPage panelWorkshopMods = GameObject.FindObjectsOfType<MainMenuPage>(true).FirstOrDefault(page => page.gameObject.name == "PanelWorkshopMods");

			// Find Source GameObjects from workshopPanel
			Transform sourceButtonList = panelWorkshopMods.transform.Find("ModList");
			Transform sourceWindow = sourceButtonList?.Find("Window");
			Transform sourceScrollbar = sourceButtonList?.Find("Scrollbar");
			Transform sourceDynamicGrid = sourceWindow?.Find("DynamicGrid");

			if(sourceButtonList == null || sourceWindow == null || sourceScrollbar == null || sourceDynamicGrid == null) {
				Debug.LogError("Source GameObjects not found in PanelWorkshopMods.");
				return;
			}

			// Find Panel which contain buttons
			Transform panelServerWindow = panelSettings.transform.Find("PanelServerWindow");

			if(panelServerWindow == null) {
				Debug.LogError("PanelServerWindow not found in PanelSettings.");
				return;
			}

			Transform oldButtonGrid = panelServerWindow.Find("ButtonGrid");
			if(oldButtonGrid == null) {
				Debug.LogError("ButtonGrid not found in PanelSettings.");
				return;
			}

			// Create ButtonList, Window and Scrollbar
			GameObject buttonListGO = new GameObject("ButtonList");
			buttonListGO.transform.SetParent(panelServerWindow, false);

			GameObject windowGO = new GameObject("Window");
			windowGO.transform.SetParent(buttonListGO.transform, false);

			GameObject scrollbarGO = new GameObject("Scrollbar");
			scrollbarGO.transform.SetParent(buttonListGO.transform, false);

			// Copy components from source templates
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

			// Create slidingArea and handle for scrollbar
			GameObject slidingAreaGO = new GameObject("Sliding Area");
			slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);
			CopyAndAddComponent<RectTransform>(sourceScrollbar.Find("Sliding Area"), slidingAreaGO).anchoredPosition = new Vector2(0, 0);

			GameObject handleGO = new GameObject("Handle");
			handleGO.transform.SetParent(slidingAreaGO.transform, false);
			Transform sourceHandle = sourceScrollbar.Find("Sliding Area/Handle");
			if(sourceHandle != null) {
				CopyAndAddComponent<RectTransform>(sourceHandle, handleGO).anchoredPosition = new Vector2(0, 0);
				CopyAndAddComponent<CanvasRenderer>(sourceHandle, handleGO);
				CopyAndAddComponent<Image>(sourceHandle, handleGO);
				CopyAndAddComponent<ColorBlindImage>(sourceHandle, handleGO);
				CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceHandle, handleGO);
			}

			// Copy scrollbar components
			RectTransform rectScrollbar = CopyAndAddComponent<RectTransform>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<CanvasRenderer>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<Image>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<Scrollbar>(sourceScrollbar, scrollbarGO).handleRect = handleGO.GetComponent<RectTransform>();
			CopyAndAddComponent<ColorBlindImage>(sourceScrollbar, scrollbarGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceScrollbar, scrollbarGO);

			// Create buttonGrid from the DynamicGrid of the source
			GameObject newButtonGrid = new GameObject("ButtonGrid");
			newButtonGrid.transform.SetParent(windowGO.transform, false);
			CopyAndAddComponent<RectTransform>(sourceDynamicGrid, newButtonGrid).anchoredPosition = new Vector2(0, 0);
			CopyAndAddComponent<GridLayoutGroup>(sourceDynamicGrid, newButtonGrid);
			CopyAndAddComponent<ContentSizeFitter>(sourceDynamicGrid, newButtonGrid);

			// Get togglegroup from old buttonGrid to new buttonList
			ToggleGroup newToggleGroup = CopyAndAddComponent<ToggleGroup>(oldButtonGrid, newButtonGrid);

			// Move buttons from old buttonGrid to new buttonList
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

			ScrollRect scrollRect = buttonListGO.GetComponent<ScrollRect>();
			scrollRect.content = newButtonGrid.GetComponent<RectTransform>();
			scrollRect.viewport = windowGO.GetComponent<RectTransform>();
			scrollRect.verticalScrollbar = scrollbarGO.GetComponent<Scrollbar>();
			scrollRect.horizontal = false;
			scrollRect.vertical = true;

			//Set position and anchors for fitting ther new window architecture
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

			// Delete old ButtonGrid
			Object.DestroyImmediate(oldButtonGrid.gameObject);

			//Only for visualising for dev
			//AddOutlineToPanel(windowGO.gameObject, Color.cyan);
			//AddOutlineToPanel(buttonListGO.gameObject, Color.green);
			//AddOutlineToPanel(sourceButtonList.gameObject, Color.green);
			//AddOutlineToPanel(sourceWindow.gameObject, Color.cyan);
			//AddOutlineToPanel(scrollbarGO.gameObject, Color.yellow);
			//AddOutlineToPanel(sourceScrollbar.gameObject, Color.yellow);
		}
		#endregion

		#region Utility Methods
		#endregion

		#region Dev Utility

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

		#endregion

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

		private void Rescale(Texture2D tex, int newWidth, int newHeight) {
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

		private void CreateChildWithComponents(Transform parent, string childName, Transform source) {
			// Création d'un nouvel objet enfant avec le nom spécifié
			GameObject child = new GameObject(childName);
			child.transform.SetParent(parent);

			// Pour chaque composant du source, ajouter et copier les valeurs dans le nouvel enfant
			foreach(Component component in source.GetComponents<Component>()) {
				System.Type componentType = component.GetType();

				// Utiliser la méthode générique CopyAndAddComponent pour gérer la copie et l'ajout
				var copyMethod = GetType().GetMethod("CopyAndAddComponent", BindingFlags.NonPublic | BindingFlags.Instance)
										.MakeGenericMethod(componentType);
				copyMethod.Invoke(this, new object[] { source, child });
			}
		}

		private T CopyComponentValues<T>(Transform source, GameObject destination) where T : Component {

			if(source == null || destination == null) {
				Debug.LogError("Source or Destination is null.");
				return null;
			}

			T originalComponent = source.GetComponent<T>();
			if(originalComponent == null) {
				Debug.LogWarning($"Source does not have a component of type {typeof(T).Name}.");
				return null;
			}

			T destinationComponent = destination.GetComponent<T>();
			if(destinationComponent == null) {
				destinationComponent = destination.AddComponent<T>();
			}

			System.Type type = typeof(T);
			List<string> consolidatedWhitelist = ZoopComponentFilterRules.Instance.BuildConsolidatedWhitelist(type);

			var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach(var field in fields) {
				string fieldName = field.Name;

				// Check if this field is in the consolidated whitelist
				if(consolidatedWhitelist.Contains(fieldName)) {
					try {
						var value = field.GetValue(originalComponent);
						field.SetValue(destinationComponent, value);
					} catch(System.Exception ex) {
						Debug.LogWarning($"Failed to copy field '{fieldName}' from {type.Name}: {ex.Message}");
					}
				}
			}

			var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach(var property in properties) {
				string propertyName = property.Name;

				if(property.CanRead && property.CanWrite && consolidatedWhitelist.Contains(propertyName)) {
					try {
						var value = property.GetValue(originalComponent);
						property.SetValue(destinationComponent, value);
					} catch(System.Exception ex) {
						Debug.LogWarning($"Failed to copy property '{propertyName}' from {type.Name}: {ex.Message}");
					}
				}
			}

			return destinationComponent;
		}

		private T CopyAndAddComponent<T>(Transform source, GameObject destination) where T : Component {
			if(source.GetComponent<T>() != null) {
				destination.AddComponent<T>();
				CopyComponentValues<T>(source, destination);
				return destination.GetComponent<T>();
			}
			return null;
		}

	}
}