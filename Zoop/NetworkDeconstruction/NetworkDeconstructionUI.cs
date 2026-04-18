using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Renders the network deconstruction UI tooltip.
/// Displays network information and validation status near the cursor.
/// </summary>
public class NetworkDeconstructionUI
{
  private Texture2D _backgroundTexture;

  /// <summary>
  /// Renders the network information tooltip.
  /// </summary>
  public void Render(Structure target, List<Structure> network, NetworkValidator.ValidationResult validation, bool isDeconstructing)
  {
    if (target == null || network == null)
      return;

    // Get network info
    int networkSize = network.Count;
    string networkType = GetNetworkTypeName(target);

    // Position near cursor
    Vector2 mousePos = Input.mousePosition;
    float posX = mousePos.x + NetworkDeconstructionConfig.TooltipOffset.x;
    float posY = Screen.height - mousePos.y + NetworkDeconstructionConfig.TooltipOffset.y;

    // Create styles
    var boxStyle = CreateBoxStyle();
    var labelStyle = CreateLabelStyle();

    // Build message with rich text formatting
    string message = BuildMessage(networkType, networkSize, validation, isDeconstructing);

    // Calculate size
    GUIContent content = new GUIContent(message);
    Vector2 contentSize = labelStyle.CalcSize(content);
    float boxWidth = contentSize.x + 20;
    float boxHeight = contentSize.y + 16;

    // Draw
    GUI.Box(new Rect(posX, posY, boxWidth, boxHeight), "", boxStyle);
    GUI.Label(new Rect(posX, posY, boxWidth, boxHeight), message, labelStyle);
  }

  private string BuildMessage(string networkType, int networkSize, NetworkValidator.ValidationResult validation, bool isDeconstructing)
  {
    var message = $"<b><color={NetworkDeconstructionConfig.Colors.NetworkTitle}>{networkType} Network</color></b>\n";
    message += $"<size={NetworkDeconstructionConfig.FontSizes.NetworkSize}>Size: {networkSize} structure{(networkSize > 1 ? "s" : "")}</size>\n\n";

    if (isDeconstructing)
    {
      // Show deconstruction in progress
      message += $"<color={NetworkDeconstructionConfig.Colors.Warning}>⚙ Deconstructing...</color>";
    }
    else if (validation.CanDeconstruct)
    {
      message += $"<color={NetworkDeconstructionConfig.Colors.Ready}>✓ Ready to deconstruct</color>\n";
      message += $"<color={NetworkDeconstructionConfig.Colors.Warning}><size={NetworkDeconstructionConfig.FontSizes.Reason}>Click to deconstruct</size></color>";
    }
    else
    {
      message += $"<color={NetworkDeconstructionConfig.Colors.Error}>✗ Cannot deconstruct</color>\n";
      message += $"<color={NetworkDeconstructionConfig.Colors.Warning}><size={NetworkDeconstructionConfig.FontSizes.Reason}>{validation.Reason}</size></color>";
    }

    return message;
  }

  private string GetNetworkTypeName(Structure structure)
  {
    if (structure is Cable)
      return "CABLE";
    if (structure is Pipe)
      return "PIPE";
    if (structure is Chute)
      return "CHUTE";

    return "UNKNOWN";
  }

  private GUIStyle CreateBoxStyle()
  {
    var style = new GUIStyle(GUI.skin.box);
    style.richText = true;
    style.fontSize = NetworkDeconstructionConfig.FontSizes.Title;
    style.alignment = TextAnchor.UpperLeft;
    style.padding = NetworkDeconstructionConfig.Padding;
    style.normal.background = GetBackgroundTexture();
    return style;
  }

  private GUIStyle CreateLabelStyle()
  {
    var style = new GUIStyle(GUI.skin.label);
    style.richText = true;
    style.fontSize = NetworkDeconstructionConfig.FontSizes.Title;
    style.alignment = TextAnchor.UpperLeft;
    style.padding = NetworkDeconstructionConfig.Padding;
    return style;
  }

  private Texture2D GetBackgroundTexture()
  {
    if (_backgroundTexture == null)
    {
      _backgroundTexture = MakeTexture(2, 2, NetworkDeconstructionConfig.BackgroundColor);
    }
    return _backgroundTexture;
  }

  private Texture2D MakeTexture(int width, int height, Color color)
  {
    Color[] pixels = new Color[width * height];
    for (int i = 0; i < pixels.Length; i++)
      pixels[i] = color;

    var texture = new Texture2D(width, height);
    texture.SetPixels(pixels);
    texture.Apply();
    return texture;
  }
}
