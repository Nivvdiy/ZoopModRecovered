using UnityEngine;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Configuration constants for network deconstruction feature.
/// </summary>
public static class NetworkDeconstructionConfig
{
  /// <summary>
  /// Maximum raycast distance to detect targeted structures.
  /// </summary>
  public const float RaycastDistance = 10f;

  /// <summary>
  /// Minimum pressure threshold for safe pipe deconstruction (in Pascals).
  /// </summary>
  public const float SafePressureThreshold = 10f;

  /// <summary>
  /// UI tooltip offset from cursor position.
  /// </summary>
  public static readonly Vector2 TooltipOffset = new Vector2(20f, 20f);

  /// <summary>
  /// UI colors for different states.
  /// </summary>
  public static class Colors
  {
    public const string NetworkTitle = "#00DDFF";  // Cyan
    public const string Ready = "#00FF00";          // Green
    public const string Error = "#FF4444";          // Red
    public const string Warning = "#FFCC00";        // Yellow/Orange
  }

  /// <summary>
  /// UI font sizes.
  /// </summary>
  public static class FontSizes
  {
    public const int Title = 16;
    public const int NetworkSize = 15;
    public const int Status = 16;
    public const int Reason = 14;
  }

  /// <summary>
  /// UI styling.
  /// </summary>
  public static readonly Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
  public static readonly RectOffset Padding = new RectOffset(10, 10, 8, 8);
}
