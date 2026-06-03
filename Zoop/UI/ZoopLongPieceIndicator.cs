using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZoopMod.Zoop.Placement;

namespace ZoopMod.Zoop.UI;

internal sealed class ZoopLongPieceIndicator
{
  private static readonly int[] DisplaySpans = { 3, 5, 10 };
  private static readonly Color FrameAllowedColor = new(0.12f, 0.75f, 0.12f, 0.95f);
  private static readonly Color FrameBlockedColor = new(0.8f, 0.2f, 0.2f, 0.95f);
  private static readonly Color PanelFallbackColor = new(0.1f, 0.1f, 0.1f, 0.85f);
  private const string PlayerStatePath = "GameCanvas/PanelStatusInfo";

  private readonly Dictionary<int, IndicatorCell> _cells = [];

  private GameObject _panel;
  private bool _isInitialized;
  private bool _requestedVisible;
  private Sprite _cachedIcon;

  private sealed class IndicatorCell
  {
    public Image FrameImage;
    public Image IconImage;
  }

  public void Update(bool visible, Structure iconSource)
  {
    _requestedVisible = visible;
    EnsureInitialized();

    if (_panel == null)
    {
      return;
    }

    var shouldShow = _requestedVisible && InventoryManager.ShowUi;
    if (_panel.activeSelf != shouldShow)
    {
      _panel.SetActive(shouldShow);
    }

    if (!shouldShow)
    {
      return;
    }

    var icon = TryResolveIcon(iconSource);
    if (icon != _cachedIcon)
    {
      _cachedIcon = icon;
      foreach (var pair in _cells)
      {
        var iconImage = pair.Value.IconImage;
        if (iconImage == null)
        {
          continue;
        }

        iconImage.sprite = icon;
        iconImage.enabled = icon != null;
      }
    }

    foreach (var span in DisplaySpans)
    {
      if (!_cells.TryGetValue(span, out var cell) || cell.FrameImage == null)
      {
        continue;
      }

      cell.FrameImage.color = ZoopLongVariantRules.IsLongSpanAllowed(span)
        ? FrameAllowedColor
        : FrameBlockedColor;
    }
  }

  private void EnsureInitialized()
  {
    if (_isInitialized)
    {
      return;
    }

    _isInitialized = true;
    CreatePanel();
  }

  private void CreatePanel()
  {
    var panelHands = GameObject.Find("GameCanvas/PanelHands");
    var gameCanvas = GameObject.Find("GameCanvas");
    if (panelHands == null || gameCanvas == null)
    {
      return;
    }

    var panelHandsRect = panelHands.GetComponent<RectTransform>();
    if (panelHandsRect == null)
    {
      return;
    }

    var highestTop = panelHandsRect.anchoredPosition.y + panelHandsRect.sizeDelta.y;
    var leftHand = GameObject.Find("GameCanvas/PanelHands/LeftHand");
    if (leftHand != null)
    {
      var leftRect = leftHand.GetComponent<RectTransform>();
      if (leftRect != null)
      {
        highestTop = Mathf.Max(highestTop,
          panelHandsRect.anchoredPosition.y + leftRect.anchoredPosition.y + leftRect.sizeDelta.y);
      }
    }

    var rightHand = GameObject.Find("GameCanvas/PanelHands/RightHand");
    if (rightHand != null)
    {
      var rightRect = rightHand.GetComponent<RectTransform>();
      if (rightRect != null)
      {
        highestTop = Mathf.Max(highestTop,
          panelHandsRect.anchoredPosition.y + rightRect.anchoredPosition.y + rightRect.sizeDelta.y);
      }
    }

    _panel = new GameObject("ZoopLongPieceIndicatorPanel");
    _panel.transform.SetParent(gameCanvas.transform, false);
    _panel.AddComponent<CanvasRenderer>();

    var panelRect = _panel.AddComponent<RectTransform>();
    panelRect.anchorMin = panelHandsRect.anchorMin;
    panelRect.anchorMax = panelHandsRect.anchorMax;
    panelRect.pivot = panelHandsRect.pivot;
    panelRect.sizeDelta = new Vector2(Mathf.Max(330f, panelHandsRect.sizeDelta.x * 1.04f), 154f);
    panelRect.anchoredPosition = new Vector2(panelHandsRect.anchoredPosition.x, highestTop + 12f);

    var panelImage = _panel.AddComponent<Image>();
    panelImage.sprite = ResolvePanelSprite();
    panelImage.type = Image.Type.Sliced;
    panelImage.color = ResolvePanelColor();

    var styleSource = ResolveTextStyleSource();

    var titleObject = new GameObject("Title");
    titleObject.transform.SetParent(_panel.transform, false);
    var titleRect = titleObject.AddComponent<RectTransform>();
    titleRect.anchorMin = new Vector2(0f, 0.72f);
    titleRect.anchorMax = new Vector2(1f, 1f);
    titleRect.offsetMin = new Vector2(8f, -2f);
    titleRect.offsetMax = new Vector2(-8f, -4f);

    var title = titleObject.AddComponent<TextMeshProUGUI>();
    title.text = ZoopText.LongPiecesTitle;
    title.alignment = TextAlignmentOptions.Center;
    title.verticalAlignment = VerticalAlignmentOptions.Middle;
    title.enableAutoSizing = false;

    if (styleSource != null)
    {
      title.font = styleSource.font;
      title.fontStyle = styleSource.fontStyle;
      title.color = styleSource.color;
      title.fontSize = Mathf.Max(11f, styleSource.fontSize * 0.72f);
    }
    else
    {
      title.fontSize = 14f;
      title.color = Color.white;
      title.fontStyle = FontStyles.Bold;
    }

    var row = new GameObject("Row");
    row.transform.SetParent(_panel.transform, false);
    var rowRect = row.AddComponent<RectTransform>();
    rowRect.anchorMin = new Vector2(0f, 0f);
    rowRect.anchorMax = new Vector2(1f, 0.76f);
    rowRect.offsetMin = new Vector2(12f, 12f);
    rowRect.offsetMax = new Vector2(-12f, -12f);

    var layout = row.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 12f;
    layout.childForceExpandWidth = true;
    layout.childForceExpandHeight = true;
    layout.childControlWidth = true;
    layout.childControlHeight = true;

    var frameSprite = ResolvePanelSprite();

    foreach (var span in DisplaySpans)
    {
      _cells[span] = CreateCell(row.transform, span, frameSprite, styleSource);
    }

    _panel.SetActive(false);
  }

  private static IndicatorCell CreateCell(Transform parent, int span, Sprite frameSprite, TextMeshProUGUI styleSource)
  {
    var root = new GameObject($"Span{span}");
    root.transform.SetParent(parent, false);

    var rootRect = root.AddComponent<RectTransform>();
    rootRect.sizeDelta = new Vector2(96f, 92f);

    var frameImage = root.AddComponent<Image>();
    frameImage.sprite = frameSprite;
    frameImage.type = Image.Type.Sliced;
    frameImage.color = FrameAllowedColor;

    var iconObject = new GameObject("Icon");
    iconObject.transform.SetParent(root.transform, false);
    var iconRect = iconObject.AddComponent<RectTransform>();
    iconRect.anchorMin = new Vector2(0f, 0.28f);
    iconRect.anchorMax = new Vector2(1f, 1f);
    iconRect.offsetMin = new Vector2(4f, -2f);
    iconRect.offsetMax = new Vector2(-4f, -4f);

    var iconImage = iconObject.AddComponent<Image>();
    iconImage.preserveAspect = true;
    iconImage.enabled = false;

    var labelObject = new GameObject("Label");
    labelObject.transform.SetParent(root.transform, false);
    var labelRect = labelObject.AddComponent<RectTransform>();
    labelRect.anchorMin = new Vector2(0f, 0f);
    labelRect.anchorMax = new Vector2(1f, 0.3f);
    labelRect.offsetMin = Vector2.zero;
    labelRect.offsetMax = Vector2.zero;

    var label = labelObject.AddComponent<TextMeshProUGUI>();
    label.text = $"X{span}";
    label.alignment = TextAlignmentOptions.Center;
    label.verticalAlignment = VerticalAlignmentOptions.Middle;
    label.enableAutoSizing = false;

    if (styleSource != null)
    {
      label.font = styleSource.font;
      label.fontStyle = styleSource.fontStyle;
      label.color = styleSource.color;
      label.fontSize = Mathf.Max(10f, styleSource.fontSize * 0.75f);
    }
    else
    {
      label.fontSize = 12f;
      label.color = Color.white;
      label.fontStyle = FontStyles.Bold;
    }

    return new IndicatorCell
    {
      FrameImage = frameImage,
      IconImage = iconImage
    };
  }

  private static TextMeshProUGUI ResolveTextStyleSource()
  {
    var playerStatePanel = GameObject.Find(PlayerStatePath);
    if (playerStatePanel == null)
    {
      return null;
    }

    var textComponents = playerStatePanel.GetComponentsInChildren<TextMeshProUGUI>();
    return textComponents.Length > 0 ? textComponents[0] : null;
  }

  private static Sprite ResolvePanelSprite()
  {
    var playerStatePanel = GameObject.Find(PlayerStatePath);
    if (playerStatePanel != null)
    {
      var sourceImage = playerStatePanel.GetComponentInChildren<Image>();
      if (sourceImage != null && sourceImage.sprite != null)
      {
        return sourceImage.sprite;
      }
    }

    return SolidSprite.Instance;
  }

  private static Color ResolvePanelColor()
  {
    var playerStatePanel = GameObject.Find(PlayerStatePath);
    if (playerStatePanel != null)
    {
      var sourceImage = playerStatePanel.GetComponentInChildren<Image>();
      if (sourceImage != null)
      {
        return sourceImage.color;
      }
    }

    return PanelFallbackColor;
  }

  private static Sprite TryResolveIcon(Structure source)
  {
    if (source == null)
    {
      return null;
    }

    if (TryReadSpriteMember(source, out var directSprite))
    {
      return directSprite;
    }

    var buildStates = source.BuildStates;
    if (buildStates != null && buildStates.Count > 0)
    {
      foreach (var buildState in buildStates)
      {
        if (buildState == null)
        {
          continue;
        }

        if (TryReadSpriteMember(buildState, out var stateSprite))
        {
          return stateSprite;
        }

        if (buildState.Tool != null && TryReadSpriteMember(buildState.Tool, out var toolSprite))
        {
          return toolSprite;
        }
      }
    }

    return null;
  }

  private static bool TryReadSpriteMember(object source, out Sprite sprite)
  {
    sprite = null;
    if (source == null)
    {
      return false;
    }

    var type = source.GetType();
    while (type != null)
    {
      var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

      foreach (var memberName in SpriteMembers.Names)
      {
        var property = type.GetProperty(memberName, flags);
        if (property?.PropertyType == typeof(Sprite))
        {
          sprite = property.GetValue(source) as Sprite;
          if (sprite != null)
          {
            return true;
          }
        }

        var field = type.GetField(memberName, flags);
        if (field?.FieldType == typeof(Sprite))
        {
          sprite = field.GetValue(source) as Sprite;
          if (sprite != null)
          {
            return true;
          }
        }
      }

      var getSprite = type.GetMethod("GetSprite", flags, null, Type.EmptyTypes, null);
      if (getSprite?.ReturnType == typeof(Sprite))
      {
        sprite = getSprite.Invoke(source, null) as Sprite;
        if (sprite != null)
        {
          return true;
        }
      }

      type = type.BaseType;
    }

    return false;
  }

  private static class SpriteMembers
  {
    public static readonly string[] Names =
    {
      "Icon",
      "icon",
      "IconSprite",
      "iconSprite",
      "Sprite",
      "sprite",
      "Thumbnail",
      "thumbnail",
      "Image",
      "image",
      "m_Icon",
      "_icon"
    };
  }

  private static class SolidSprite
  {
    private static Sprite _instance;
    public static Sprite Instance
    {
      get
      {
        if (_instance == null)
        {
          _instance = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        return _instance;
      }
    }
  }
}
