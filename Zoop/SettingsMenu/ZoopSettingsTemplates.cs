using Assets.Scripts.UI;
using UnityEngine;
using UnityEngine.UI;
using ColorBlindUtility.UGUI;
using TMPro;
using System.IO;

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

		public GameObject GetMenuSettingButton(string localizationKey, Transform parent) {
			return GetMenuSettingButton(localizationKey, parent, localizationKey);
		}

		public GameObject GetMenuSettingButtonWithImage(string localizationKey, Transform parent, string iconFileName) {
			return GetMenuSettingButton(localizationKey, parent, localizationKey, iconFileName);
		}

		public GameObject GetMenuSettingButton(string localizationKey, Transform parent, string buttonName, string iconFileName = null) {
			// Define the resources for the toggle
			DefaultControls.Resources toggleResources = new DefaultControls.Resources();

			// Copy background and checkmark from the template button
			GameObject templateButton = parent.Find("ButtonGameplay").gameObject;
			toggleResources.background = templateButton.GetComponent<Image>().sprite;  // Assuming this is the correct sprite
																					   // Set checkmark resource here if needed

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
	}
}