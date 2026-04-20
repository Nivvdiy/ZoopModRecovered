using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Renders the bulk deconstruction UI tooltip.
/// Displays bulk information and validation status near the cursor.
/// </summary>
public class BulkDeconstructionUI
{
  private Texture2D _backgroundTexture;

  /// <summary>
  /// Renders the bulk information tooltip.
  /// </summary>
  public void Render(Structure target, List<Structure> bulk, BulkValidator.ValidationResult validation, bool isDeconstructing)
  {
    if (target == null || bulk == null)
      return;

    // Get bulk info
    int bulkSize = bulk.Count;
    string bulkType = GetBulkTypeName(target);

    // Position near cursor
    Vector2 mousePos = Input.mousePosition;
    float posX = mousePos.x + BulkDeconstructionConfig.TooltipOffset.x;
    float posY = Screen.height - mousePos.y + BulkDeconstructionConfig.TooltipOffset.y;

    // Create styles
    var boxStyle = CreateBoxStyle();
    var labelStyle = CreateLabelStyle();

    // Build message with rich text formatting
    string message = BuildMessage(bulkType, bulkSize, validation, isDeconstructing);

    // Calculate size
    GUIContent content = new GUIContent(message);
    Vector2 contentSize = labelStyle.CalcSize(content);
    float boxWidth = contentSize.x + 20;
    float boxHeight = contentSize.y + 16;

    // Draw
    GUI.Box(new Rect(posX, posY, boxWidth, boxHeight), "", boxStyle);
    GUI.Label(new Rect(posX, posY, boxWidth, boxHeight), message, labelStyle);
  }

  private string BuildMessage(string bulkType, int bulkSize, BulkValidator.ValidationResult validation, bool isDeconstructing)
  {
    var message = $"<b><color={BulkDeconstructionConfig.Colors.BulkTitle}>{bulkType} Bulk</color></b>\n";
    message += $"<size={BulkDeconstructionConfig.FontSizes.BulkSize}>Size: {bulkSize} structure{(bulkSize > 1 ? "s" : "")}</size>\n\n";

    if (isDeconstructing)
    {
      // Show deconstruction in progress
      message += $"<color={BulkDeconstructionConfig.Colors.Warning}>⚙ Deconstructing...</color>";
    }
    else if (validation.CanDeconstruct)
    {
      message += $"<color={BulkDeconstructionConfig.Colors.Ready}>✓ Ready to deconstruct</color>\n";
      message += $"<color={BulkDeconstructionConfig.Colors.Warning}><size={BulkDeconstructionConfig.FontSizes.Reason}>Click to deconstruct</size></color>";
    }
    else
    {
      message += $"<color={BulkDeconstructionConfig.Colors.Error}>✗ Cannot deconstruct</color>\n";
      message += $"<color={BulkDeconstructionConfig.Colors.Warning}><size={BulkDeconstructionConfig.FontSizes.Reason}>{validation.Reason}</size></color>";
    }

    return message;
  }

  private string GetBulkTypeName(Structure structure)
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
    style.fontSize = BulkDeconstructionConfig.FontSizes.Title;
    style.alignment = TextAnchor.UpperLeft;
    style.padding = BulkDeconstructionConfig.Padding;
    style.normal.background = GetBackgroundTexture();
    return style;
  }

  private GUIStyle CreateLabelStyle()
  {
    var style = new GUIStyle(GUI.skin.label);
    style.richText = true;
    style.fontSize = BulkDeconstructionConfig.FontSizes.Title;
    style.alignment = TextAnchor.UpperLeft;
    style.padding = BulkDeconstructionConfig.Padding;
    return style;
  }

  private Texture2D GetBackgroundTexture()
  {
    if (_backgroundTexture == null)
    {
      _backgroundTexture = MakeTexture(2, 2, BulkDeconstructionConfig.BackgroundColor);
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
