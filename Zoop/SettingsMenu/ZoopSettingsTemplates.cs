using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using Assets.Scripts.Util;
using ColorBlindUtility.UGUI;
using LeTai.Asset.TranslucentImage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;
using Util;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Header;
using Object = UnityEngine.Object;

namespace ZoopMod.Zoop.SettingsMenu {
	public class ZoopSettingsTemplates {

		#region Singleton with lock security
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

		private ZoopSettingsTemplates() {
		}
		#endregion

		#region Menu Element Creation
		/// <summary>
		/// Add new button to settings menu button grid
		/// </summary>
		/// <param name="parentButtonGrid">The parent transform where the button grid is located.</param>
		/// <param name="sourceButton">The sourceName button to be used as a template.</param>
		/// <param name="settingsButtonName">The name of the settings button GameObject.</param>
		/// <param name="settingsButtonLocalizationKey">The localization key for the settings button text.</param>
		/// <param name="settingsButtonIconFileName">The file name of the icon to be used for the settings button (optional).</param>
		/// <returns>The created button GameObject.</returns>
		public GameObject AddMenuSettingsButton(Transform parentButtonGrid, Transform sourceButton, string settingsButtonName, string settingsButtonLocalizationKey, string settingsButtonIconFileName = null) {


			if(sourceButton == null) {
				Debug.LogError("ButtonGameplay not found in PanelSettings");
			}

			Transform srcToggled = sourceButton.Find("Toggled");
			Transform srcButtonIcon = sourceButton.Find("ButtonIcon");
			Transform srcButtonText = sourceButton.Find("ButtonText");

			DefaultControls.Resources toggleResources = new DefaultControls.Resources{
				standard = sourceButton.GetComponent<Image>().sprite,
				checkmark = srcToggled.GetComponent<Image>().sprite
			};

			Transform button = DefaultControls.CreateToggle(toggleResources).transform;
			Transform buttonIcon = button.Find("Background");
			Transform buttonToggled = buttonIcon.Find("Checkmark");
			Transform buttonText = button.Find("Label");


			button.name = "Button" + settingsButtonName;
			buttonToggled.name = "Toggled";
			buttonIcon.name = "ButtonIcon";
			buttonText.name = "ButtonText";

			button.transform.SetParent(parentButtonGrid, false);
			Toggle buttonToggle = CopyComponentValues<Toggle>(sourceButton, button.gameObject);
			Image buttonImg = CopyAndAddComponent<Image>(sourceButton, button.gameObject);
			CopyAndAddComponent<Animator>(sourceButton, button.gameObject);
			CopyAndAddComponent<UIAudioComponent>(sourceButton, button.gameObject);
			CopyAndAddComponent<ColorBlindImage>(sourceButton, button.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceButton, button.gameObject);

			buttonToggled.SetParent(button, false);
			buttonToggled.SetAsFirstSibling();
			buttonText.transform.SetParent(button, false);

			CopyComponentValues<RectTransform>(sourceButton, button.gameObject);
			CopyComponentValues<RectTransform>(srcToggled, buttonToggled.gameObject);
			CopyComponentValues<RectTransform>(srcButtonIcon, buttonIcon.gameObject);
			CopyComponentValues<RectTransform>(srcButtonText, buttonText.gameObject);

			Image toggledImg = CopyComponentValues<Image>(srcToggled, buttonToggled.gameObject);
			Image buttonIconImg = CopyComponentValues<Image>(srcButtonIcon, buttonIcon.gameObject);

			CopyAndAddComponent<ColorBlindImage>(srcToggled, buttonToggled.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcToggled, buttonToggled.gameObject);

			CopyAndAddComponent<ColorBlindImage>(srcButtonIcon, buttonIcon.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcButtonIcon, buttonIcon.gameObject);

			GameObject.DestroyImmediate(buttonText.GetComponent<Text>());
			TextMeshProUGUI buttonTextTMP = CopyAndAddComponent<TextMeshProUGUI>(srcButtonText, buttonText.gameObject);
			LocalizedText buttonTextLocalizedText = buttonText.gameObject.AddComponent<LocalizedText>();

			buttonToggle.isOn = false;

			// Modify the elementName and localization
			if(buttonTextLocalizedText != null) {
				buttonTextLocalizedText.TextMesh = buttonTextTMP;
				buttonTextLocalizedText.StringKey = settingsButtonLocalizationKey;
				buttonTextLocalizedText.DefaultFont = srcButtonText.GetComponent<LocalizedText>().DefaultFont;
				buttonTextLocalizedText.Refresh();
			}

			// Load the icon from a PNG file if provided
			if(!string.IsNullOrEmpty(settingsButtonIconFileName)) {
				if(buttonIconImg != null) {
					Sprite iconSprite = LoadSpriteFromFile(settingsButtonIconFileName, 32, 32);
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

			return button.gameObject;
		}

		/// <summary>
		/// Adds a new page to the settings menu page grid.
		/// </summary>
		/// <param name="parentMenuSettings">The parent transform where the menu settings are located.</param>
		/// <param name="sourcePage">The sourceName page to be used as a template.</param>
		/// <param name="pageName">The name of the page GameObject.</param>
		/// <returns>The created page GameObject.</returns>
		public GameObject AddMenuSettingsPage(Transform parentMenuSettings, Transform sourcePage, string pageName) {

			if(sourcePage == null) {
				Debug.LogError("Gameplay not found in PanelSettings");
			}

			DefaultControls.Resources panelResources = new DefaultControls.Resources(){
				background = sourcePage.GetComponent<Image>().sprite
			};

			GameObject newPage = DefaultControls.CreatePanel(panelResources);
			newPage.transform.SetParent(parentMenuSettings, false);
			newPage.name = pageName;
			CopyComponentValues<RectTransform>(sourcePage, newPage);
			CopyComponentValues<Image>(sourcePage, newPage);
			CopyAndAddComponent<ScrollRect>(sourcePage, newPage);
			CopyAndAddComponent<VerticalLayoutGroup>(sourcePage, newPage);
			//CopyAndAddComponent<ContentSizeFitter>(sourcePage, newPage);
			CopyAndAddComponent<ColorBlindImage>(sourcePage, newPage);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourcePage, newPage);
			newPage.SetActive(false);

			return newPage;
		}

		/// <summary>
		/// Adds a section to the settings page of the mod.
		/// </summary>
		/// <param name="parentSettingsPage">The parent transform where the settings page is located.</param>
		/// <param name="sourceSection">The sourceName section to be used as a template.</param>
		/// <param name="panelGOName">The name of the panel GameObject.</param>
		/// <param name="titleLocalizationKey">The localization key for the panel title.</param>
		/// <returns>The created section GameObject.</returns>
		public GameObject AddPageSettingsSection(Transform parentSettingsPage, Transform sourceSection, string panelGOName, string titleLocalizationKey) {

			if(sourceSection == null) {
				Debug.LogError("TitleDisplayTools not found in PanelSettings");
			}

			DefaultControls.Resources panelResources = new DefaultControls.Resources();

			GameObject newPanel = DefaultControls.CreatePanel(panelResources);
			newPanel.transform.SetParent(parentSettingsPage, false);
			newPanel.name = panelGOName;
			CopyComponentValues<RectTransform>(sourceSection, newPanel);
			CopyComponentValues<CanvasRenderer>(sourceSection, newPanel);
			CopyAndAddComponent<VerticalLayoutGroup>(sourceSection, newPanel);
			CopyAndAddComponent<ContentSizeFitter>(sourceSection, newPanel);
			GameObject.Destroy(newPanel.GetComponent<Image>());

			AddSectionSettingsTitle(newPanel.transform, sourceSection, titleLocalizationKey);

			return newPanel;
		}

		/// <summary>
		/// Adds a text newElement to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName text newElement to be used as a template.</param>
		/// <param name="elementName">The name of the text GameObject.</param>
		/// <param name="nameLocalizationKey">The localization key for the text content.</param>
		/// <returns>The created newElement text GameObject.</returns>
		public GameObject AddSectionSettingsText(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey) {

			if(sourceElement == null) {
				Debug.LogError("Text not found in PanelSettings");
			}

			return CreateTextElement(parentSettingsPanel, sourceElement, elementName, nameLocalizationKey);
		}

		/// <summary>
		/// Adds a boolean newElement (toggle) to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName toggle newElement to be used as a template.</param>
		/// <param name="elementName">The name of the boolean GameObject.</param>
		/// <param name="nameLocalizationKey">The localization key for the boolean toggle.</param>
		/// <param name="defaultValue">The default value of the boolean toggle.</param>
		/// <returns>The created newElement boolean GameObject.</returns>
		public GameObject AddSectionSettingsBoolean(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey, bool defaultValue) {

			if(sourceElement == null) {
				Debug.LogError("Dropdown not found in PanelSettings");
			}

			GameObject newElement = new GameObject(elementName);
			newElement.transform.SetParent(parentSettingsPanel, false);
			CopyAndAddComponent<RectTransform>(sourceElement, newElement);
			CopyAndAddComponent<CanvasRenderer>(sourceElement, newElement);
			CopyAndAddComponent<Button>(sourceElement, newElement);
			CopyAndAddComponent<Animator>(sourceElement, newElement);
			//SettingItem settingItem = CopyAndAddComponent<SettingItem>(sourceElement, newElement); //TODO: Add custom settings for mods specific settings, have to be dynamic
			CopyAndAddComponent<UIAudioComponent>(sourceElement, newElement);

			AddElementSettingsName(newElement.transform, sourceElement.Find("Name"), nameLocalizationKey);

			Transform srcCheckbox = sourceElement.Find("Checkbox");
			Transform srcBackground = srcCheckbox.Find("Background");
			Transform srcCheckmark = srcBackground.Find("Checkmark");

			DefaultControls.Resources checkboxResources = new DefaultControls.Resources{
				standard = srcBackground.GetComponent<Image>().sprite,
				checkmark = srcCheckmark.GetComponent<Image>().sprite
			};

			GameObject checkboxGO = DefaultControls.CreateToggle(checkboxResources);
			checkboxGO.transform.SetParent(newElement.transform, false);
			CopyComponentValues<Toggle>(srcCheckbox, checkboxGO);
			CopyAndAddComponent<Animator>(srcCheckbox, checkboxGO);
			CopyAndAddComponent<UIAudioComponent>(srcCheckbox, checkboxGO);

			Transform elementCheckbox = checkboxGO.transform;
			Transform elementBackground = elementCheckbox.Find("Background");
			Transform elementCheckmark = elementBackground.Find("Checkmark");

			GameObject.Destroy(elementCheckbox.Find("Label").gameObject);

			CopyComponentValues<RectTransform>(srcCheckbox, checkboxGO);
			CopyComponentValues<RectTransform>(srcBackground, elementBackground.gameObject);
			CopyComponentValues<RectTransform>(srcCheckmark, elementCheckmark.gameObject);

			CopyComponentValues<Image>(srcBackground, elementBackground.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcBackground, elementBackground.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcBackground, elementBackground.gameObject);

			CopyComponentValues<Image>(srcCheckmark, elementCheckmark.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcCheckmark, elementCheckmark.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcCheckmark, elementCheckmark.gameObject);

			return newElement;
		}

		/// <summary>
		/// Adds a number (int) newElement (settingsSlider) to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName settingsSlider newElement to be used as a template.</param>
		/// <param name="elementName">The name of the number GameObject.</param>
		/// <param name="nameLocalizationKey">The localization key for the number settingsSlider.</param>
		/// <param name="defaultValue">The default value of the number settingsSlider.</param>
		/// <param name="maxValue">The maximum value of the number settingsSlider.</param>
		/// <param name="minValue">The minimum value of the number settingsSlider.</param>
		/// <returns>The created newElement number GameObject.</returns>
		public GameObject AddSectionSettingsNumber(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey, int defaultValue, int maxValue, int minValue) {
			return AddSectionSettingsNumberInternal(parentSettingsPanel, sourceElement, elementName, nameLocalizationKey, defaultValue, maxValue, minValue);
		}

		/// <summary>
		/// Adds a number (float) newElement (settingsSlider) to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName settingsSlider newElement to be used as a template.</param>
		/// <param name="elementName">The name of the number GameObject.</param>
		/// <param name="nameLocalizationKey">The localization key for the number settingsSlider.</param>
		/// <param name="defaultValue">The default value of the number settingsSlider.</param>
		/// <param name="maxValue">The maximum value of the number settingsSlider.</param>
		/// <param name="minValue">The minimum value of the number settingsSlider.</param>
		/// <returns>The created newElement number GameObject.</returns>
		public GameObject AddSectionSettingsNumber(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey, float defaultValue, float maxValue, float minValue) {
			return AddSectionSettingsNumberInternal(parentSettingsPanel, sourceElement, elementName, nameLocalizationKey, defaultValue, maxValue, minValue);
		}

		/// <summary>
		/// Adds a string list newElement (dropdown) to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName dropdown newElement to be used as a template.</param>
		/// <param name="elementName">The name of the list GameObject.</param>
		/// <param name="nameLocalizationKey">The localization key for the list dropdown.</param>
		/// <param name="choiceList">The list of choices to be included in the dropdown.</param>
		/// <returns>The created newElement string list GameObject.</returns>
		public GameObject AddSectionSettingsStringList(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey, List<string> choiceList) {

			if(sourceElement == null) {
				Debug.LogError("Dropdown not found in PanelSettings");
			}

			GameObject newElement = new GameObject(elementName);
			newElement.transform.SetParent(parentSettingsPanel, false);
			CopyAndAddComponent<RectTransform>(sourceElement, newElement);
			CopyAndAddComponent<CanvasRenderer>(sourceElement, newElement);
			CopyAndAddComponent<Button>(sourceElement, newElement);
			CopyAndAddComponent<Animator>(sourceElement, newElement);
			//SettingItem settingItem = CopyAndAddComponent<SettingItem>(sourceElement, newElement); //TODO: Add custom settings for mods specific settings, have to be dynamic
			CopyAndAddComponent<UIAudioComponent>(sourceElement, newElement);

			AddElementSettingsName(newElement.transform, sourceElement.Find("Name"), nameLocalizationKey);

			Transform srcDropdown = sourceElement.Find("Dropdown");
			Transform srcLabel = srcDropdown.Find("Label");
			Transform srcArrow = srcDropdown.Find("Arrow");
			Transform srcTemplate = srcDropdown.Find("Template");
			Transform srcViewport = srcTemplate.Find("Viewport");
			Transform srcContent = srcViewport.Find("Content");
			Transform srcItem = srcContent.Find("Item");
			Transform srcItemBackground = srcItem.Find("Item Background");
			Transform srcItemCheckmark = srcItem.Find("Item Checkmark");
			Transform srcItemLabel = srcItem.Find("Item Label");
			Transform srcScrollbar = srcTemplate.Find("Scrollbar");
			Transform srcSlidingArea = srcScrollbar.Find("Sliding Area");
			Transform srcHandle = srcSlidingArea.Find("Handle");

			TMP_DefaultControls.Resources dropdownResources = new TMP_DefaultControls.Resources{
				standard = srcDropdown.GetComponent<Image>().sprite,
				dropdown = srcArrow.GetComponent<Image>().sprite,
				mask = srcViewport.GetComponent<Image>().sprite,
				checkmark = srcItemCheckmark.GetComponent<Image>().sprite,
				background = srcScrollbar.GetComponent<Image>().sprite
			};

			GameObject dropdownGO = TMP_DefaultControls.CreateDropdown(dropdownResources);
			dropdownGO.transform.SetParent(newElement.transform, false);
			CopyComponentValues<Image>(srcDropdown, dropdownGO);
			CopyAndAddComponent<Animator>(srcDropdown, dropdownGO);
			CopyAndAddComponent<ColorBlindImage>(srcDropdown, dropdownGO);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcDropdown, dropdownGO);

			Transform elementDropdown = dropdownGO.transform;
			Transform elementLabel = elementDropdown.Find("Label");
			Transform elementArrow = elementDropdown.Find("Arrow");
			Transform elementTemplate = elementDropdown.Find("Template");
			Transform elementViewport = elementTemplate.Find("Viewport");
			Transform elementContent = elementViewport.Find("Content");
			Transform elementItem = elementContent.Find("Item");
			Transform elementItemBackground = elementItem.Find("Item Background");
			Transform elementItemCheckmark = elementItem.Find("Item Checkmark");
			Transform elementItemLabel = elementItem.Find("Item Label");
			Transform elementScrollbar = elementTemplate.Find("Scrollbar");
			Transform elementSlidingArea = elementScrollbar.Find("Sliding Area");
			Transform elementHandle = elementSlidingArea.Find("Handle");

			CopyComponentValues<RectTransform>(srcDropdown, dropdownGO);
			CopyComponentValues<RectTransform>(srcLabel, elementLabel.gameObject);
			CopyComponentValues<RectTransform>(srcArrow, elementArrow.gameObject);
			CopyComponentValues<RectTransform>(srcTemplate, elementTemplate.gameObject);
			CopyComponentValues<RectTransform>(srcViewport, elementViewport.gameObject);
			CopyComponentValues<RectTransform>(srcContent, elementContent.gameObject);
			CopyComponentValues<RectTransform>(srcItem, elementItem.gameObject);
			CopyComponentValues<RectTransform>(srcItemBackground, elementItemBackground.gameObject);
			CopyComponentValues<RectTransform>(srcItemCheckmark, elementItemCheckmark.gameObject);
			CopyComponentValues<RectTransform>(srcItemLabel, elementItemLabel.gameObject);
			CopyComponentValues<RectTransform>(srcScrollbar, elementScrollbar.gameObject);
			CopyComponentValues<RectTransform>(srcSlidingArea, elementSlidingArea.gameObject);
			CopyComponentValues<RectTransform>(srcHandle, elementHandle.gameObject);

			//settingItem.Selectable = dropdownGO.GetComponent<TMP_Dropdown>();

			CopyComponentValues<TextMeshProUGUI>(srcLabel, elementLabel.gameObject);

			CopyComponentValues<Image>(srcArrow, elementArrow.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcArrow, elementArrow.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcArrow, elementArrow.gameObject);

			Image oldImage = elementTemplate.GetComponent<Image>();
			if(oldImage != null) {
				GameObject.DestroyImmediate(oldImage);
			}
			CopyAndAddComponent<TranslucentImage>(srcTemplate, elementTemplate.gameObject);
			CopyComponentValues<ScrollRect>(srcTemplate, elementTemplate.gameObject);
			CopyAndAddComponent<Canvas>(srcTemplate, elementTemplate.gameObject);
			CopyAndAddComponent<GraphicRaycaster>(srcTemplate, elementTemplate.gameObject);
			CopyAndAddComponent<CanvasGroup>(srcTemplate, elementTemplate.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcTemplate, elementTemplate.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcTemplate, elementTemplate.gameObject);

			CopyComponentValues<Image>(srcViewport, elementViewport.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcViewport, elementViewport.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcViewport, elementViewport.gameObject);

			CopyComponentValues<Toggle>(srcItem, elementItem.gameObject);
			CopyAndAddComponent<UIAudioComponent>(srcItem, elementItem.gameObject);

			CopyComponentValues<Image>(srcItemBackground, elementItemBackground.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcItemBackground, elementItemBackground.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcItemBackground, elementItemBackground.gameObject);

			CopyComponentValues<Image>(srcItemCheckmark, elementItemCheckmark.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcItemCheckmark, elementItemCheckmark.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcItemCheckmark, elementItemCheckmark.gameObject);

			TextMeshProUGUI TMPText = CopyComponentValues<TextMeshProUGUI>(srcItemLabel, elementItemLabel.gameObject);
			TMPText.alignment = TextAlignmentOptions.MidlineLeft;

			CopyComponentValues<Image>(srcScrollbar, elementScrollbar.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcScrollbar, elementScrollbar.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcScrollbar, elementScrollbar.gameObject);

			CopyComponentValues<Image>(srcHandle, elementHandle.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcHandle, elementHandle.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcHandle, elementHandle.gameObject);

			TMP_Dropdown dropdown = dropdownGO.GetComponent<TMP_Dropdown>();
			dropdown.ClearOptions();
			dropdown.AddOptions(choiceList);
			dropdown.value = 0;
			dropdown.Select();
			dropdown.RefreshShownValue();

			return newElement;
		}

		/// <summary>
		/// Adds a string newElement (input field) to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName input field newElement to be used as a template.</param>
		/// <param name="elementName">The name of the string GameObject.</param>
		/// <param name="nameLocalizationKey">The localization key for the string input field.</param>
		/// <param name="placeholderText">The placeholderText text for the input field.</param>
		/// <param name="defaultValue">The default value of the input field.</param>
		/// <returns>The created newElement string input field GameObject.</returns>
		public GameObject AddSectionSettingsString(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey, string placeholderText, TMP_InputField.ContentType contentType,string defaultValue) {

			if(sourceElement == null) {
				Debug.LogError("InputField not found in PanelSettings");
			}

			GameObject newElementGO = new GameObject(elementName);
			newElementGO.transform.SetParent(parentSettingsPanel, false);
			CopyAndAddComponent<RectTransform>(sourceElement, newElementGO);
			CopyAndAddComponent<CanvasRenderer>(sourceElement, newElementGO);
			CopyAndAddComponent<Button>(sourceElement, newElementGO);
			CopyAndAddComponent<Animator>(sourceElement, newElementGO);
			//SettingItem settingItem = CopyAndAddComponent<SettingItem>(sourceElement, newElementGO);
			CopyAndAddComponent<ToggleVisibleOnHover>(sourceElement, newElementGO);

			AddElementSettingsName(newElementGO.transform, sourceElement.Find("Name"), nameLocalizationKey);

			Transform srcInputField = sourceElement.GetComponentInChildren<TMP_InputField>(true).transform; //.Find("ServerNameInput");
			Transform srcTextArea = srcInputField.Find("Text Area");
			Transform srcPlaceholder = srcTextArea.Find("Placeholder");
			Transform srcText = srcTextArea.Find("Text");

			TMP_DefaultControls.Resources inputFieldResources = new TMP_DefaultControls.Resources{
				inputField = srcInputField.GetComponent<Image>().sprite
			};

			Transform inputField = TMP_DefaultControls.CreateInputField(inputFieldResources).transform;
			Transform textArea = inputField.Find("Text Area");
			Transform placeholder = textArea.Find("Placeholder");
			Transform text = textArea.Find("Text");

			inputField.transform.SetParent(newElementGO.transform, false);
			CopyComponentValues<Image>(sourceElement, inputField.gameObject);
			TMP_InputField tmpInputField = CopyComponentValues<TMP_InputField>(sourceElement, inputField.gameObject);
			CopyAndAddComponent<Animator>(sourceElement, inputField.gameObject);
			CopyAndAddComponent<ColorBlindImage>(sourceElement, inputField.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceElement, inputField.gameObject);

			CopyComponentValues<RectTransform>(srcInputField, inputField.gameObject);
			CopyComponentValues<RectTransform>(srcTextArea, textArea.gameObject);
			CopyComponentValues<RectTransform>(srcPlaceholder, placeholder.gameObject);
			CopyComponentValues<RectTransform>(srcText, text.gameObject);

			CopyComponentValues<RectMask2D>(srcTextArea, textArea.gameObject);

			CopyComponentValues<TextMeshProUGUI>(srcPlaceholder, placeholder.gameObject);

			CopyComponentValues<TextMeshProUGUI>(srcText, text.gameObject);

			tmpInputField.contentType = contentType;
			tmpInputField.inputType = contentType == TMP_InputField.ContentType.Password ? TMP_InputField.InputType.Password : TMP_InputField.InputType.Standard;
			tmpInputField.text = defaultValue;

			return newElementGO;
		}

		/// <summary>
		/// Adds a divider newElement to a settings section.
		/// </summary>
		/// <param name="parentSettingsPanel">The parent transform where the settings panel is located.</param>
		/// <param name="sourceElement">The sourceName divider newElement to be used as a template.</param>
		/// <returns>The created newElement divider GameObject.</returns>
		public GameObject AddSectionSettingsDivider(Transform parentSettingsPanel, Transform sourceElement) {

			if(sourceElement == null) {
				Debug.LogError("Divider not found in PanelSettings");
			}

			DefaultControls.Resources dividerResources = new DefaultControls.Resources(){
				background = sourceElement.GetComponent<Image>().sprite
			};

			GameObject newElement = DefaultControls.CreatePanel(dividerResources);
			newElement.transform.SetParent(parentSettingsPanel, false);
			CopyComponentValues<RectTransform>(sourceElement, newElement);
			CopyComponentValues<Image>(sourceElement, newElement);
			CopyAndAddComponent<ColorBlindImage>(sourceElement, newElement);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(sourceElement, newElement);

			return newElement;
		}

		//Private methods for new elements
		private GameObject AddSectionSettingsNumberInternal<T>(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey, T defaultValue, T maxValue, T minValue) {

			GameObject newElementGO = new GameObject(elementName);
			newElementGO.transform.SetParent(parentSettingsPanel, false);
			CopyAndAddComponent<RectTransform>(sourceElement, newElementGO);
			CopyAndAddComponent<CanvasRenderer>(sourceElement, newElementGO);
			CopyAndAddComponent<Button>(sourceElement, newElementGO);
			CopyAndAddComponent<Animator>(sourceElement, newElementGO);
			SettingItem settingItem = CopyAndAddComponent<SettingItem>(sourceElement, newElementGO);
			CopyAndAddComponent<ToggleVisibleOnHover>(sourceElement, newElementGO);

			AddElementSettingsName(newElementGO.transform, sourceElement.Find("Name"), nameLocalizationKey);

			Transform srcSlider = sourceElement.Find("SettingsSlider");
			Transform srcBackground = srcSlider.Find("Background");
			Transform srcFillArea = srcSlider.Find("Fill Area");
			Transform srcFill = srcFillArea.Find("Fill");
			Transform srcHandleSlideArea = srcSlider.Find("Handle Slide Area");
			Transform srcHandle = srcHandleSlideArea.Find("Handle");
			Transform srcSliderValue = srcSlider.Find("SliderValue");
			Transform srcTextArea = srcSliderValue.Find("Text Area");
			Transform srcCaret = srcTextArea.Find("Caret");
			Transform srcPlaceholder = srcTextArea.Find("Placeholder");
			Transform srcText = srcTextArea.Find("Text");

			DefaultControls.Resources sliderResources = new DefaultControls.Resources{
				standard = srcFill.GetComponent<Image>().sprite,
				background = srcBackground.GetComponent<Image>().sprite,
				knob = srcHandle.GetComponent<Image>().sprite
			};

			TMP_DefaultControls.Resources textAreaResources = new TMP_DefaultControls.Resources{
				inputField = srcSliderValue.GetComponent<Image>().sprite
			};

			Transform settingsSlider = DefaultControls.CreateSlider(sliderResources).transform;
			Transform background = settingsSlider.Find("Background");
			Transform fillArea = settingsSlider.Find("Fill Area");
			Transform fill = fillArea.Find("Fill");
			Transform handleSlideArea = settingsSlider.Find("Handle Slide Area");
			Transform handle = handleSlideArea.Find("Handle");
			Transform sliderValue = TMP_DefaultControls.CreateInputField(textAreaResources).transform;
			Transform textArea = sliderValue.Find("Text Area");
			Transform caret = new GameObject("Caret", typeof(TMP_SelectionCaret), typeof(LayoutElement)).transform;
			Transform placeholder = textArea.Find("Placeholder");
			Transform text = textArea.Find("Text");

			settingsSlider.SetParent(newElementGO.transform, false);
			sliderValue.SetParent(settingsSlider, false);
			caret.SetParent(textArea, false);
			caret.SetAsFirstSibling();

			settingsSlider.name = "SettingsSlider";
			sliderValue.name = "SliderValue";

			CopyComponentValues<RectTransform>(srcSlider, settingsSlider.gameObject);
			CopyComponentValues<RectTransform>(srcBackground, background.gameObject);
			CopyComponentValues<RectTransform>(srcFillArea, fillArea.gameObject);
			CopyComponentValues<RectTransform>(srcFill, fill.gameObject);
			CopyComponentValues<RectTransform>(srcHandleSlideArea, handleSlideArea.gameObject);
			CopyComponentValues<RectTransform>(srcHandle, handle.gameObject);
			CopyComponentValues<RectTransform>(srcSliderValue, sliderValue.gameObject);
			CopyComponentValues<RectTransform>(srcTextArea, textArea.gameObject);
			CopyComponentValues<RectTransform>(srcCaret, caret.gameObject);
			CopyComponentValues<RectTransform>(srcPlaceholder, placeholder.gameObject);
			CopyComponentValues<RectTransform>(srcText, text.gameObject);

			Slider slider = CopyComponentValues<Slider>(srcSlider, settingsSlider.gameObject);
			SetSliderValueTMP sliderValueTMP = slider.gameObject.AddComponent<SetSliderValueTMP>();
			CopyAndAddComponent<ToggleVisibleOnHover>(srcSlider, settingsSlider.gameObject);
			CopyAndAddComponent<UIAudioComponent>(srcSlider, settingsSlider.gameObject);

			CopyComponentValues<Image>(srcBackground, background.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcBackground, background.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcBackground, background.gameObject);

			CopyComponentValues<Image>(srcFill, fill.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcFill, fill.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcFill, fill.gameObject);

			CopyComponentValues<Image>(srcHandle, handle.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcHandle, handle.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcHandle, handle.gameObject);

			CopyComponentValues<Image>(srcSliderValue, sliderValue.gameObject);
			TMP_InputField inputField = CopyComponentValues<TMP_InputField>(srcSliderValue, sliderValue.gameObject);
			CopyAndAddComponent<Animator>(srcSliderValue, sliderValue.gameObject);
			CopyAndAddComponent<ColorBlindImage>(srcSliderValue, sliderValue.gameObject);
			CopyAndAddComponent<ColorBlindUtility.UGUI.ColorBlindUtility>(srcSliderValue, sliderValue.gameObject);

			GameObject.Destroy(srcTextArea.GetComponent<RectMask2D>());

			CopyComponentValues<TMP_SelectionCaret>(srcCaret, caret.gameObject);
			CopyComponentValues<LayoutElement>(srcCaret, caret.gameObject);

			TextMeshProUGUI placeholderTMP = CopyComponentValues<TextMeshProUGUI>(srcPlaceholder, placeholder.gameObject);

			CopyComponentValues<TextMeshProUGUI>(srcText, text.gameObject);

			inputField.contentType = typeof(T) == typeof(int) ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
			inputField.characterLimit = typeof(T) == typeof(int) ? Convert.ToInt32(maxValue).ToString().Length : Convert.ToSingle(maxValue).ToString("F3").Length;
			inputField.onEndEdit.AddListener((string input) => {
				if(string.IsNullOrEmpty(input))
					return;

				if(typeof(T) == typeof(int) && int.TryParse(input, out int value)) {
					if(value < Convert.ToInt32(minValue)) {
						inputField.text = minValue.ToString();
					} else if(value > Convert.ToInt32(maxValue)) {
						inputField.text = maxValue.ToString();
					}
				} else if(typeof(T) == typeof(float) && float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out float valueF)) {
					if(valueF < Convert.ToSingle(minValue)) {
						inputField.text = Convert.ToSingle(minValue).ToString("F3", CultureInfo.CurrentCulture);
					} else if(valueF > Convert.ToSingle(maxValue)) {
						inputField.text = Convert.ToSingle(maxValue).ToString("F3", CultureInfo.CurrentCulture);
					} else {
						inputField.text = valueF.ToString("F3", CultureInfo.CurrentCulture);
					}
				}

				sliderValueTMP.OnFinishedInputValue();
			});

			inputField.onValidateInput = (string textValue, int charIndex, char addedChar) => {
				string decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

				if(addedChar == ',' || addedChar == '.') {
					addedChar = decimalSeparator[0];
				}

				if(char.IsDigit(addedChar) || addedChar == decimalSeparator[0]) {
					string newText = textValue.Insert(charIndex, addedChar.ToString());

					string[] parts = newText.Split(decimalSeparator[0]);
					if(parts.Length == 2 && parts[1].Length > 3) {
						return '\0';
					}

					string textWithoutSeparator = parts[0];
					if(textWithoutSeparator.Length > maxValue.ToString().Replace(decimalSeparator, "").Length) {
						return '\0';
					}

					return addedChar;
				}
				return '\0';
			};


			settingItem.Selectable = slider;
			settingItem.SetSliderValue = sliderValueTMP;
			settingItem.SettingType = SettingType.AutoSave;
			settingItem.Setup();

			sliderValueTMP.TargetField = inputField;
			sliderValueTMP.TargetSlider = slider;

			slider.minValue = System.Convert.ToSingle(minValue);
			slider.maxValue = System.Convert.ToSingle(maxValue);
			slider.value = System.Convert.ToSingle(defaultValue);
			slider.wholeNumbers = typeof(T) == typeof(int);

			slider.onValueChanged.AddListener((float value) => {
				string valueStr;
				if(typeof(T) == typeof(int)) {
					valueStr = value.ToString();
				} else if(typeof(T) == typeof(float)) {
					valueStr = value.ToString("F3");
				} else {
					valueStr = minValue.ToString();
				}

				if(float.TryParse(valueStr, out float limitedValue)) {
					slider.value = limitedValue;
				}
			});

			return newElementGO;
		}

		private GameObject AddSectionSettingsTitle(Transform parentSettingsPanel, Transform sourcePanel, string titleLocalizationKey) {

			Transform sourceTitle = sourcePanel.Find("Title");
			if(sourceTitle == null) {
				Debug.LogError("Title not found in PanelSettings");
			}

			return CreateTextElement(parentSettingsPanel, sourceTitle, "Title", titleLocalizationKey);
		}

		private GameObject CreateTextElement(Transform parentSettingsPanel, Transform sourceElement, string elementName, string nameLocalizationKey) {

			GameObject newElement = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
			newElement.transform.SetParent(parentSettingsPanel, false);
			newElement.name = elementName;
			CopyComponentValues<RectTransform>(sourceElement, newElement);
			TextMeshProUGUI textMesh = CopyAndAddComponent<TextMeshProUGUI>(sourceElement, newElement);
			LocalizedText localizedText = CopyAndAddComponent<LocalizedText>(sourceElement, newElement);

			if(localizedText != null) {
				localizedText.TextMesh = textMesh;
				localizedText.StringKey = nameLocalizationKey;
				localizedText.Refresh();
			}

			return newElement;
		}

		private GameObject AddElementSettingsName(Transform parentSettingsElement, Transform sourceName, string nameLocalizationKey) {
			GameObject newNameGO = new GameObject("Name");
			newNameGO.transform.SetParent(parentSettingsElement, false);
			CopyAndAddComponent<RectTransform>(sourceName, newNameGO);
			CopyAndAddComponent<CanvasRenderer>(sourceName, newNameGO);
			TextMeshProUGUI newNameTMP = CopyAndAddComponent<TextMeshProUGUI>(sourceName, newNameGO);
			LocalizedText newNameLocalizedText = CopyAndAddComponent<LocalizedText>(sourceName, newNameGO);
			// Modify the elementName and localization
			if(newNameLocalizedText != null) {
				newNameLocalizedText.TextMesh = newNameTMP;
				newNameLocalizedText.StringKey = nameLocalizationKey;
				newNameLocalizedText.Refresh();
			} else {
				newNameTMP.text = nameLocalizationKey;
			}
			return newNameGO;
		}
		#endregion

		#region Panel settings button grid transformation for scrollbar
		/// <summary>
		/// Adds a scroll bar to the settings menu.
		/// In case of too much new buttons in the settings menu
		/// </summary>
		/// <param name="panelSettings">The settings panel to which the scroll bar will be added.</param>
		public void AddScrollBarToSettingsMenu(ref MainMenuPage panelSettings) {
			if(panelSettings == null) {
				Debug.LogError("panelSettings is null.");
				return;
			}

			MainMenuPage panelWorkshopMods = GameObject.FindObjectsOfType<MainMenuPage>(true).FirstOrDefault(page => page.gameObject.name == "PanelWorkshopMods");

			if(panelWorkshopMods == null) {
				Debug.LogError("PanelWorkshopMods not found.");
				return;
			}

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

			// Copy components from sourceName templates
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
			CopyAndAddComponent<RectTransform>(sourceScrollbar.Find("Sliding Area"), slidingAreaGO).anchoredPosition = Vector2.zero;

			GameObject handleGO = new GameObject("Handle");
			handleGO.transform.SetParent(slidingAreaGO.transform, false);
			Transform sourceHandle = sourceScrollbar.Find("Sliding Area/Handle");
			if(sourceHandle != null) {
				CopyAndAddComponent<RectTransform>(sourceHandle, handleGO).anchoredPosition = Vector2.zero;
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

			// Create buttonGrid from the DynamicGrid of the sourceName
			GameObject newButtonGrid = new GameObject("ButtonGrid");
			newButtonGrid.transform.SetParent(windowGO.transform, false);
			CopyAndAddComponent<RectTransform>(sourceDynamicGrid, newButtonGrid).anchoredPosition = Vector2.zero;
			CopyAndAddComponent<GridLayoutGroup>(sourceDynamicGrid, newButtonGrid);
			CopyAndAddComponent<ContentSizeFitter>(sourceDynamicGrid, newButtonGrid);

			// Get toggle group from old buttonGrid to new buttonList
			ToggleGroup newToggleGroup = CopyAndAddComponent<ToggleGroup>(oldButtonGrid, newButtonGrid);

			// Move buttons from old buttonGrid to new buttonList
			var children = new List<Transform>();
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

			//Set position and anchors for fitting the new window architecture
			Vector2 oldOffsetMax = oldButtonGrid.GetComponent<RectTransform>().offsetMax;
			Vector2 oldOffsetMin = oldButtonGrid.GetComponent<RectTransform>().offsetMin;

			rectWindow.anchorMax = Vector2.up;
			rectWindow.offsetMax = new Vector2(buttonSize.width, 0);
			rectWindow.offsetMin = Vector2.zero;

			rectScrollbar.anchorMin = Vector2.right;
			rectScrollbar.offsetMax = new Vector2(0, rectWindow.offsetMax.y);
			rectScrollbar.offsetMin = new Vector2(-10, rectWindow.offsetMin.y);

			rectButtonList.anchorMax = Vector2.up;
			rectButtonList.offsetMax = new Vector2(oldOffsetMin.x + buttonSize.width + rectScrollbar.rect.width + 5, oldOffsetMax.y);
			rectButtonList.offsetMin = new Vector2(oldOffsetMin.x, sourceButtonList.GetComponent<RectTransform>().offsetMin.y);

			newButtonGrid.GetComponent<GridLayoutGroup>().cellSize = buttonSize.size;

			// Delete old ButtonGrid
			Object.DestroyImmediate(oldButtonGrid.gameObject);
		}
		#endregion

		#region Utility Methods
		private Sprite LoadSpriteFromFile(string fileName, int expectedWidth, int expectedHeight, float pixelsPerUnit = 100.0f) {

			string modPath = Path.GetDirectoryName(Instance.GetType().Assembly.Location);
			string filePath = Path.Combine(modPath, "GameData", "Images", fileName + ".png");

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
			var consolidatedWhitelist = ZoopComponentFilterRules.Instance.BuildConsolidatedWhitelist(type);

			var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach(FieldInfo field in fields) {
				string fieldName = field.Name;

				// Check if this field is in the consolidated whitelist
				if(consolidatedWhitelist.Contains(fieldName)) {
					try {
						object value = field.GetValue(originalComponent);
						field.SetValue(destinationComponent, value);
					} catch(System.Exception ex) {
						Debug.LogWarning($"Failed to copy field '{fieldName}' from {type.Name}: {ex.Message}");
					}
				}
			}

			var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach(PropertyInfo property in properties) {
				string propertyName = property.Name;

				if(property.CanRead && property.CanWrite && consolidatedWhitelist.Contains(propertyName)) {
					try {
						object value = property.GetValue(originalComponent);
						property.SetValue(destinationComponent, value);
					} catch(System.Exception ex) {
						Debug.LogWarning($"Failed to copy property '{propertyName}' from {type.Name}: {ex.Message}");
					}
				}
			}

			return destinationComponent;
		}

		private T CopyAndAddComponent<T>(Transform source, GameObject destination) where T : Component {
			if(source == null) {
				Debug.LogError("Can't copy component sourceName is null.");
				return null;
			}
			if(destination == null) {
				Debug.LogError("Can't copy component destination is null.");
				return null;
			}

			if(source.GetComponent<T>() != null) {
				if(destination.GetComponent<T>() == null) {
					destination.AddComponent<T>();
				} else {
					Debug.LogWarning($"Destination already has a component of type {typeof(T).Name}.");
				}
				CopyComponentValues<T>(source, destination);
				return destination.GetComponent<T>();
			} else {
				Debug.LogWarning($"Source does not have a component of type {typeof(T).Name}.");
			}
			return null;
		}
		#endregion

	}
}