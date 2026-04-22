using UnityEngine;
using TMPro;
using Assets.Scripts.Inventory;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// UI indicator that shows when bulk deconstruction mode is active.
/// Displays a styled panel similar to player status panels in the game.
/// </summary>
public class BulkDeconstructionStatusIndicator
{
  private GameObject _statusPanel;
  private TextMeshProUGUI _titleText;
  private TextMeshProUGUI _statusText;
  private bool _isInitialized;
  private bool _isVisible;

  // UI paths based on Stationeers UI structure - try multiple possible paths
  private static readonly string[] InventoryPaths = new[]
  {
    "GameCanvas/PanelHands", // ✅ CONFIRMED WORKING PATH
    "GameCanvas/InventoryWindow",
    "GameCanvas/Inventory",
    "GameCanvas/PlayerInventory",
    "GameCanvas/HandInventory",
    "GameCanvas/HotbarWindow",
    "GameCanvas/SlotHand0", // Direct slot reference as fallback
  };

  private const string PlayerStatePath = "GameCanvas/PanelStatusInfo"; // Pour récupérer les styles
  private const string WastePanelPath = "GameCanvas/StatusIcons/Waste/Panel"; // Fallback reference

  // UI Configuration
  private const float TextLineSpacing = 5f; // Espace entre les deux lignes
  private const float PanelPadding = 8f; // Marge intérieure haut/bas
  private static readonly Vector2 FallbackPosition = new Vector2(0f, 100f); // Position absolue si inventaire non trouvé
  private const string TitleText = "Bulk Deconstruction";
  private const string StatusText = "Activated";
  private static readonly Color StatusActiveColor = new Color(0.0f, 1.0f, 0.0f); // Vert pour "Activated"

  /// <summary>
  /// Initialize the status indicator UI.
  /// Should be called once during game initialization.
  /// </summary>
  public void Initialize()
  {
    ZoopLog.Debug("[BulkDeconstruction] Status indicator Initialize() called");

    if (_isInitialized)
    {
      ZoopLog.Warn("[BulkDeconstruction] Status indicator already initialized");
      return;
    }

    try
    {
      ZoopLog.Debug("[BulkDeconstruction] Creating status panel...");
      CreateStatusPanel();
      _isInitialized = true;
      SetVisible(false); // Start hidden
      ZoopLog.Info("[BulkDeconstruction] Status indicator initialized successfully");
    }
    catch (System.Exception ex)
    {
      ZoopLog.Error(ex, "[BulkDeconstruction] Failed to initialize status indicator");
    }
  }

  /// <summary>
  /// Show or hide the status indicator.
  /// </summary>
  public void SetVisible(bool visible)
  {
    if (!_isInitialized)
    {
      ZoopLog.Warn("[BulkDeconstruction] Cannot set visibility - indicator not initialized");
      return;
    }

    ZoopLog.Debug($"[BulkDeconstruction] SetVisible({visible}) called");
    _isVisible = visible;

    if (_statusPanel != null)
    {
      // Hide when UI is disabled or when not in active mode
      bool shouldShow = visible && InventoryManager.ShowUi;
      ZoopLog.Debug($"[BulkDeconstruction] Setting panel active: {shouldShow} (visible={visible}, ShowUi={InventoryManager.ShowUi})");
      _statusPanel.SetActive(shouldShow);
    }
    else
    {
      ZoopLog.Warn("[BulkDeconstruction] Status panel is null in SetVisible");
    }
  }

  /// <summary>
  /// Update the indicator state. Call this every frame to handle UI visibility changes.
  /// </summary>
  public void Update()
  {
    if (!_isInitialized || _statusPanel == null)
      return;

    // Respect game's UI visibility setting
    if (_isVisible)
    {
      bool shouldShow = InventoryManager.ShowUi;
      if (_statusPanel.activeSelf != shouldShow)
      {
        _statusPanel.SetActive(shouldShow);
      }
    }
  }

  /// <summary>
  /// Creates the status panel by detecting inventory position and copying styles.
  /// </summary>
  private void CreateStatusPanel()
  {
    ZoopLog.Debug("[BulkDeconstruction] Searching for hand inventory UI...");

    // Find PanelHands
    GameObject handInventory = GameObject.Find("GameCanvas/PanelHands");
    if (handInventory == null)
    {
      ZoopLog.Error("[BulkDeconstruction] Could not find GameCanvas/PanelHands");
      return;
    }

    RectTransform panelHandsRect = handInventory.GetComponent<RectTransform>();
    if (panelHandsRect == null)
    {
      ZoopLog.Error("[BulkDeconstruction] PanelHands has no RectTransform");
      return;
    }

    ZoopLog.Debug($"[BulkDeconstruction] PanelHands position: {panelHandsRect.anchoredPosition}, size: {panelHandsRect.sizeDelta}");

    // Check LeftHand and RightHand to find the highest element
    float highestTop = panelHandsRect.anchoredPosition.y + panelHandsRect.sizeDelta.y;
    string highestElement = "PanelHands";

    GameObject leftHand = GameObject.Find("GameCanvas/PanelHands/LeftHand");
    if (leftHand != null)
    {
      RectTransform leftRect = leftHand.GetComponent<RectTransform>();
      if (leftRect != null)
      {
        float leftTop = panelHandsRect.anchoredPosition.y + leftRect.anchoredPosition.y + leftRect.sizeDelta.y;
        ZoopLog.Debug($"[BulkDeconstruction] LeftHand top: {leftTop}");
        if (leftTop > highestTop)
        {
          highestTop = leftTop;
          highestElement = "LeftHand";
        }
      }
    }

    GameObject rightHand = GameObject.Find("GameCanvas/PanelHands/RightHand");
    if (rightHand != null)
    {
      RectTransform rightRect = rightHand.GetComponent<RectTransform>();
      if (rightRect != null)
      {
        float rightTop = panelHandsRect.anchoredPosition.y + rightRect.anchoredPosition.y + rightRect.sizeDelta.y;
        ZoopLog.Debug($"[BulkDeconstruction] RightHand top: {rightTop}");
        if (rightTop > highestTop)
        {
          highestTop = rightTop;
          highestElement = "RightHand";
        }
      }
    }

    ZoopLog.Info($"[BulkDeconstruction] Highest element: {highestElement} at top={highestTop}");

    // Find GameCanvas as parent for our panel
    GameObject gameCanvas = GameObject.Find("GameCanvas");
    if (gameCanvas == null)
    {
      ZoopLog.Error("[BulkDeconstruction] Could not find GameCanvas");
      return;
    }

    // Create our custom panel with the highest top position
    CreateCustomPanel(gameCanvas, panelHandsRect);
    ConfigurePanel(panelHandsRect, highestTop);
  }

  /// <summary>
  /// Creates a custom panel from scratch with proper styling.
  /// </summary>
  private void CreateCustomPanel(GameObject parent, RectTransform inventoryRect)
  {
    ZoopLog.Debug("[BulkDeconstruction] Creating custom panel...");

    // Create the panel GameObject
    _statusPanel = new GameObject("BulkDeconstructStatusPanel");
    _statusPanel.transform.SetParent(parent.transform, false);

    // Add RectTransform for positioning
    var rectTransform = _statusPanel.AddComponent<RectTransform>();

    // Add Canvas Renderer for rendering
    _statusPanel.AddComponent<CanvasRenderer>();

    // Add background image - try to copy style from player state panel
    var bgImage = _statusPanel.AddComponent<UnityEngine.UI.Image>();

    // Try to get style from an existing panel
    GameObject playerStatePanel = GameObject.Find(PlayerStatePath);
    if (playerStatePanel != null)
    {
      var existingImage = playerStatePanel.GetComponentInChildren<UnityEngine.UI.Image>();
      if (existingImage != null)
      {
        bgImage.sprite = existingImage.sprite;
        bgImage.color = existingImage.color;
        bgImage.type = existingImage.type;
        ZoopLog.Debug("[BulkDeconstruction] Copied background style from player state panel");
      }
    }
    else
    {
      // Fallback to simple black background
      bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
    }

    ZoopLog.Debug("[BulkDeconstruction] Custom panel created");
  }

  /// <summary>
  /// Configure the panel's appearance and positioning based on inventory position.
  /// </summary>
  private void ConfigurePanel(RectTransform inventoryRect, float highestTop)
  {
    RectTransform rectTransform = _statusPanel.GetComponent<RectTransform>();
    if (rectTransform == null)
    {
      ZoopLog.Error("[BulkDeconstruction] Panel has no RectTransform");
      return;
    }

    // Try to get text style from existing UI FIRST
    TextMeshProUGUI styleSource = null;
    float textFontSize = 16f; // Default fallback

    GameObject playerStatePanel = GameObject.Find(PlayerStatePath);
    if (playerStatePanel != null)
    {
      var textComponents = playerStatePanel.GetComponentsInChildren<TextMeshProUGUI>();
      if (textComponents.Length > 0)
      {
        styleSource = textComponents[0];
        textFontSize = styleSource.fontSize;
        ZoopLog.Debug($"[BulkDeconstruction] Found style source with fontSize={textFontSize}");
      }
    }

    // Calculate exact panel height based on text size
    float lineHeight = textFontSize * 1.2f; // Text height with line spacing
    float totalTextHeight = (lineHeight * 2) + TextLineSpacing; // Two lines + spacing between
    float panelHeight = totalTextHeight + (PanelPadding * 2); // Add top/bottom padding

    rectTransform.anchorMin = inventoryRect.anchorMin;
    rectTransform.anchorMax = inventoryRect.anchorMax;
    rectTransform.pivot = inventoryRect.pivot;

    // Size: use inventory width, calculated height
    Vector2 panelSize = new Vector2(inventoryRect.sizeDelta.x, panelHeight);
    rectTransform.sizeDelta = panelSize;

    // Position: 10px margin above the highest element
    Vector2 panelPosition = new Vector2(
      inventoryRect.anchoredPosition.x,
      highestTop + 10f // 10px margin above highest element
    );
    rectTransform.anchoredPosition = panelPosition;

    ZoopLog.Debug($"[BulkDeconstruction] Panel size: {panelSize} (calculated from fontSize={textFontSize}, lineHeight={lineHeight})");
    ZoopLog.Debug($"[BulkDeconstruction] Panel positioned at {panelPosition} (10px above highest element at {highestTop})");

    // Create title text (line 1: "Bulk Deconstruction")
    GameObject titleObject = new GameObject("TitleText");
    titleObject.transform.SetParent(_statusPanel.transform, false);

    RectTransform titleRect = titleObject.AddComponent<RectTransform>();
    titleRect.anchorMin = new Vector2(0f, 0.5f);
    titleRect.anchorMax = new Vector2(1f, 1f);
    titleRect.pivot = new Vector2(0.5f, 0.5f);
    titleRect.sizeDelta = Vector2.zero;
    titleRect.anchoredPosition = Vector2.zero; // Centered in top half

    _titleText = titleObject.AddComponent<TextMeshProUGUI>();
    _titleText.text = TitleText;
    _titleText.alignment = TextAlignmentOptions.Center;
    _titleText.verticalAlignment = VerticalAlignmentOptions.Middle; // Perfect vertical centering

    // Copy EXACT style from source
    if (styleSource != null)
    {
      _titleText.font = styleSource.font;
      _titleText.fontSize = styleSource.fontSize;
      _titleText.fontStyle = styleSource.fontStyle;
      _titleText.color = styleSource.color;
      ZoopLog.Debug("[BulkDeconstruction] Copied title style from existing UI");
    }
    else
    {
      _titleText.fontSize = textFontSize;
      _titleText.fontStyle = FontStyles.Bold;
      _titleText.color = new Color(0.9f, 0.9f, 0.9f);
    }

    _titleText.enableAutoSizing = false;

    // Create status text (line 2: "Activated" in green - SAME STYLE except color)
    GameObject statusObject = new GameObject("StatusText");
    statusObject.transform.SetParent(_statusPanel.transform, false);

    RectTransform statusRect = statusObject.AddComponent<RectTransform>();
    statusRect.anchorMin = new Vector2(0f, 0f);
    statusRect.anchorMax = new Vector2(1f, 0.5f);
    statusRect.pivot = new Vector2(0.5f, 0.5f);
    statusRect.sizeDelta = Vector2.zero;
    statusRect.anchoredPosition = new Vector2(0f, 10f); // Remonté de 10px

    _statusText = statusObject.AddComponent<TextMeshProUGUI>();
    _statusText.text = StatusText;
    _statusText.alignment = TextAlignmentOptions.Center;
    _statusText.verticalAlignment = VerticalAlignmentOptions.Middle; // Perfect vertical centering

    // Copy EXACT SAME style from source (same as title), only change color to green
    if (styleSource != null)
    {
      _statusText.font = styleSource.font;
      _statusText.fontSize = styleSource.fontSize;
      _statusText.fontStyle = styleSource.fontStyle;
      _statusText.color = StatusActiveColor; // Only difference: green color
      ZoopLog.Debug("[BulkDeconstruction] Copied status style from existing UI (green color)");
    }
    else
    {
      _statusText.fontSize = textFontSize;
      _statusText.fontStyle = FontStyles.Bold;
      _statusText.color = StatusActiveColor;
    }

    _statusText.enableAutoSizing = false;

    ZoopLog.Debug("[BulkDeconstruction] Panel configured with exact text sizing and styling");
  }

  /// <summary>
  /// Update the status text (second line) displayed on the indicator.
  /// </summary>
  public void SetStatusText(string text)
  {
    if (_statusText != null)
    {
      _statusText.text = text;
    }
  }

  /// <summary>
  /// Update the title text (first line) displayed on the indicator.
  /// </summary>
  public void SetTitleText(string text)
  {
    if (_titleText != null)
    {
      _titleText.text = text;
    }
  }

  /// <summary>
  /// Update the color of the indicator texts.
  /// </summary>
  public void SetColor(Color color)
  {
    if (_titleText != null)
    {
      _titleText.color = color;
    }
    if (_statusText != null)
    {
      _statusText.color = color;
    }
  }

  /// <summary>
  /// Cleanup resources.
  /// </summary>
  public void Cleanup()
  {
    if (_statusPanel != null)
    {
      Object.Destroy(_statusPanel);
      _statusPanel = null;
      _titleText = null;
      _statusText = null;
    }
    _isInitialized = false;
    _isVisible = false;
  }
}
