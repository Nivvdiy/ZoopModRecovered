using UnityEngine;

namespace ZoopMod.Zoop.Core;

internal static class ZoopPositionUtility
{
  internal const float PositionToleranceSqr = 0.0001f;

  public static bool IsSameZoopPosition(Vector3 first, Vector3 second)
  {
    return Vector3.SqrMagnitude(first - second) < PositionToleranceSqr;
  }

  /// <summary>
  /// Maps a world-space position to a small-grid cell key by scaling up by 2 and rounding,
  /// so that 0.5-unit snapped positions produce unique integer keys.
  /// </summary>
  public static Vector3Int ToSmallGridCellKey(Vector3 position)
  {
    return new Vector3Int(
      Mathf.RoundToInt(position.x * 2f),
      Mathf.RoundToInt(position.y * 2f),
      Mathf.RoundToInt(position.z * 2f));
  }
}
