using UnityEngine;

namespace ZoopMod.Zoop.Core;

internal static class ZoopPositionUtility
{
  internal const float PositionToleranceSqr = 0.0001f;

  public static bool IsSameZoopPosition(Vector3 first, Vector3 second)
  {
    return Vector3.SqrMagnitude(first - second) < PositionToleranceSqr;
  }
}
