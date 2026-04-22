using UnityEngine;
using TMPro;
using Assets.Scripts;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Modifies the in-world tooltip to display bulk deconstruction information.
/// </summary>
public class BulkDeconstructionTooltip
{
  private const string TooltipPath = "GameCanvas/PanelInWorldToolTip/ToolTip";
  private const string ItemTitlePath = "GameCanvas/PanelInWorldToolTip/ToolTip/ItemTitle";
  private const string InfoFlashpointPath = "GameCanvas/PanelInWorldToolTip/ToolTip/InfoFlashpoint";

  private GameObject _bulkSizeInfo;
  private GameObject _bulkStatusInfo;
  private TextMeshProUGUI _bulkSizeText;
  private TextMeshProUGUI _bulkSizeValue;
  private TextMeshProUGUI _bulkStatusText;
  private TextMeshProUGUI _bulkStatusValue;
  private TextMeshProUGUI _itemTitleText;

  private string _originalTitleText;
  private bool _isModified;

  // Optimization: cache last update values to avoid redundant updates
  private string _cachedStructureType;
  private int _cachedBulkSize;
  private bool _cachedIsValid;

  /// <summary>
  /// Update the tooltip with bulk deconstruction information.
  /// </summary>
  public void UpdateTooltip(string structureType, int bulkSize, bool isValid)
  {
    // Optimization: skip update if values haven't changed
    if (_cachedStructureType == structureType && _cachedBulkSize == bulkSize && _cachedIsValid == isValid)
      return;

    _cachedStructureType = structureType;
    _cachedBulkSize = bulkSize;
    _cachedIsValid = isValid;

    GameObject tooltip = GameObject.Find(TooltipPath);
    if (tooltip == null)
      return;

    // Get or create bulk info elements
    if (_bulkSizeInfo == null || _bulkStatusInfo == null)
    {
      CreateBulkInfoElements(tooltip);
    }

    // Modify item title
    GameObject itemTitle = GameObject.Find(ItemTitlePath);
    if (itemTitle != null && _itemTitleText == null)
    {
      _itemTitleText = itemTitle.GetComponent<TextMeshProUGUI>();
    }

    if (_itemTitleText != null)
    {
      if (!_isModified)
      {
        _originalTitleText = _itemTitleText.text;
        _isModified = true;
      }
      _itemTitleText.text = $"{structureType} Bulk";
    }

    // Update bulk size
    if (_bulkSizeValue != null)
    {
      _bulkSizeValue.text = bulkSize.ToString();
    }

    // Update validation status
    if (_bulkStatusValue != null)
    {
      _bulkStatusValue.text = isValid ? "OK" : "INVALID";
      _bulkStatusValue.color = isValid ? new Color(0.0f, 1.0f, 0.0f) : new Color(1.0f, 0.0f, 0.0f);
    }

    // Show bulk info elements
    if (_bulkSizeInfo != null) _bulkSizeInfo.SetActive(true);
    if (_bulkStatusInfo != null) _bulkStatusInfo.SetActive(true);
  }

  /// <summary>
  /// Restore the original tooltip when not in bulk mode.
  /// </summary>
  public void RestoreOriginalTooltip()
  {
    // Clear cache
    _cachedStructureType = null;
    _cachedBulkSize = 0;
    _cachedIsValid = false;

    if (_isModified && _itemTitleText != null && !string.IsNullOrEmpty(_originalTitleText))
    {
      _itemTitleText.text = _originalTitleText;
      _isModified = false;
    }

    if (_bulkSizeInfo != null) _bulkSizeInfo.SetActive(false);
    if (_bulkStatusInfo != null) _bulkStatusInfo.SetActive(false);
  }

  /// <summary>
  /// Create the bulk info UI elements in the tooltip.
  /// </summary>
  private void CreateBulkInfoElements(GameObject tooltip)
  {
    // Find the style reference (InfoFlashpoint) to clone
    GameObject styleReference = GameObject.Find(InfoFlashpointPath);
    if (styleReference == null)
    {
      ZoopLog.Warn("[BulkTooltip] Could not find style reference at InfoFlashpoint");
      return;
    }

    // Get ItemTitle to position our elements after it
    GameObject itemTitle = GameObject.Find(ItemTitlePath);
    if (itemTitle == null)
    {
      ZoopLog.Warn("[BulkTooltip] Could not find ItemTitle");
      return;
    }

    int itemTitleIndex = itemTitle.transform.GetSiblingIndex();

    // Clone InfoFlashpoint for bulk size info
    _bulkSizeInfo = Object.Instantiate(styleReference, tooltip.transform);
    _bulkSizeInfo.name = "BulkSizeInfo";
    _bulkSizeInfo.transform.SetSiblingIndex(itemTitleIndex + 1);

    // Update text content for bulk size
    _bulkSizeText = _bulkSizeInfo.GetComponent<TextMeshProUGUI>();
    if (_bulkSizeText != null)
    {
      _bulkSizeText.text = "Network Size:";
    }

    // Get value text component
    Transform valueTransform = _bulkSizeInfo.transform.Find("InfoValue");
    if (valueTransform != null)
    {
      _bulkSizeValue = valueTransform.GetComponent<TextMeshProUGUI>();
      if (_bulkSizeValue != null)
      {
        _bulkSizeValue.text = "";
      }
    }

    // Clone InfoFlashpoint for bulk status info
    _bulkStatusInfo = Object.Instantiate(styleReference, tooltip.transform);
    _bulkStatusInfo.name = "BulkStatusInfo";
    _bulkStatusInfo.transform.SetSiblingIndex(itemTitleIndex + 2);

    // Update text content for bulk status
    _bulkStatusText = _bulkStatusInfo.GetComponent<TextMeshProUGUI>();
    if (_bulkStatusText != null)
    {
      _bulkStatusText.text = "Status:";
    }

    // Get value text component
    Transform statusValueTransform = _bulkStatusInfo.transform.Find("InfoValue");
    if (statusValueTransform != null)
    {
      _bulkStatusValue = statusValueTransform.GetComponent<TextMeshProUGUI>();
      if (_bulkStatusValue != null)
      {
        _bulkStatusValue.text = "";
      }
    }

    // Hide both initially
    _bulkSizeInfo.SetActive(false);
    _bulkStatusInfo.SetActive(false);
  }

  /// <summary>
  /// Cleanup tooltip modifications.
  /// </summary>
  public void Cleanup()
  {
    RestoreOriginalTooltip();

    if (_bulkSizeInfo != null) Object.Destroy(_bulkSizeInfo);
    if (_bulkStatusInfo != null) Object.Destroy(_bulkStatusInfo);

    _bulkSizeInfo = null;
    _bulkStatusInfo = null;
    _bulkSizeText = null;
    _bulkSizeValue = null;
    _bulkStatusText = null;
    _bulkStatusValue = null;
    _itemTitleText = null;
  }
}
