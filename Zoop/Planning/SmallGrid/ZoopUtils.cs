using System;
using UnityEngine;
using ZoopMod.Zoop.Planning;

namespace ZoopMod.Zoop.Planning.SmallGrid;

// Helper lookups for corner pieces. The rest of the zoop code works in terms of
// axis directions; this class translates those directions into a transition id
// and the final mesh rotation needed for that corner.
public static class ZoopUtils
{
  // Transition names follow the pattern "xPyN" = from +X, then turn toward -Y.
  // This lets the caller describe a corner in axis/sign terms without manually
  // re-encoding every possible turn.
  public static ZoopTransition GetTransition(ZoopDirection zoopDirectionFrom, bool increasingFrom,
    ZoopDirection zoopDirectionTo, bool increasingTo)
  {
    return (zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo) switch
    {
      (ZoopDirection.x, true, ZoopDirection.y, true) => ZoopTransition.xPyP,
      (ZoopDirection.x, true, ZoopDirection.y, false) => ZoopTransition.xPyN,
      (ZoopDirection.x, false, ZoopDirection.y, true) => ZoopTransition.xNyP,
      (ZoopDirection.x, false, ZoopDirection.y, false) => ZoopTransition.xNyN,
      (ZoopDirection.x, true, ZoopDirection.z, true) => ZoopTransition.xPzP,
      (ZoopDirection.x, true, ZoopDirection.z, false) => ZoopTransition.xPzN,
      (ZoopDirection.x, false, ZoopDirection.z, true) => ZoopTransition.xNzP,
      (ZoopDirection.x, false, ZoopDirection.z, false) => ZoopTransition.xNzN,
      (ZoopDirection.y, true, ZoopDirection.x, true) => ZoopTransition.yPxP,
      (ZoopDirection.y, true, ZoopDirection.x, false) => ZoopTransition.yPxN,
      (ZoopDirection.y, false, ZoopDirection.x, true) => ZoopTransition.yNxP,
      (ZoopDirection.y, false, ZoopDirection.x, false) => ZoopTransition.yNxN,
      (ZoopDirection.y, true, ZoopDirection.z, true) => ZoopTransition.yPzP,
      (ZoopDirection.y, true, ZoopDirection.z, false) => ZoopTransition.yPzN,
      (ZoopDirection.y, false, ZoopDirection.z, true) => ZoopTransition.yNzP,
      (ZoopDirection.y, false, ZoopDirection.z, false) => ZoopTransition.yNzN,
      (ZoopDirection.z, true, ZoopDirection.x, true) => ZoopTransition.zPxP,
      (ZoopDirection.z, true, ZoopDirection.x, false) => ZoopTransition.zPxN,
      (ZoopDirection.z, false, ZoopDirection.x, true) => ZoopTransition.zNxP,
      (ZoopDirection.z, false, ZoopDirection.x, false) => ZoopTransition.zNxN,
      (ZoopDirection.z, true, ZoopDirection.y, true) => ZoopTransition.zPyP,
      (ZoopDirection.z, true, ZoopDirection.y, false) => ZoopTransition.zPyN,
      (ZoopDirection.z, false, ZoopDirection.y, true) => ZoopTransition.zNyP,
      (ZoopDirection.z, false, ZoopDirection.y, false) => ZoopTransition.zNyN,
      _ => throw new ArgumentException("Invalid zoop transition.")
    };
  }

  // Returns the base rotation for a corner piece, then layers prefab-specific
  // offsets on top so different models can reuse the same transition mapping.
  public static Quaternion GetCornerRotation(ZoopDirection zoopDirectionFrom, bool increasingFrom,
    ZoopDirection zoopDirectionTo, bool increasingTo, float xOffset, float yOffset, float zOffset)
  {
    switch (GetTransition(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo))
    {
      // Each paired case is the same physical corner approached from the opposite
      // direction, so both transitions resolve to the same final orientation.
      case ZoopTransition.xPyP:
      case ZoopTransition.yNxN:
        return Quaternion.Euler(xOffset + 90f, yOffset + 0.0f, zOffset + 0.0f);
      case ZoopTransition.xPyN:
      case ZoopTransition.yPxN:
        return Quaternion.Euler(xOffset + -90f, yOffset + 0.0f, zOffset + 0.0f);
      case ZoopTransition.xNyP:
      case ZoopTransition.yNxP:
        return Quaternion.Euler(xOffset + 90f, yOffset + -180.0f, zOffset + 0.0f);
      case ZoopTransition.xNyN:
      case ZoopTransition.yPxP:
        return Quaternion.Euler(xOffset + -90f, yOffset + -180.0f, zOffset + 0.0f);
      case ZoopTransition.xPzP:
      case ZoopTransition.zNxN:
        return Quaternion.Euler(xOffset + 180.0f, yOffset + 0.0f, zOffset + 0.0f);
      case ZoopTransition.xPzN:
      case ZoopTransition.zPxN:
        return Quaternion.Euler(xOffset + 0.0f, yOffset + 0f, zOffset + 0.0f);
      case ZoopTransition.xNzP:
      case ZoopTransition.zNxP:
        return Quaternion.Euler(xOffset + 180.0f, yOffset + 90f, zOffset + 0.0f);
      case ZoopTransition.xNzN:
      case ZoopTransition.zPxP:
        return Quaternion.Euler(xOffset + 0.0f, yOffset + -90f, zOffset + 0.0f);
      case ZoopTransition.yPzP:
      case ZoopTransition.zNyN:
        return Quaternion.Euler(xOffset + -90.0f, yOffset + 90f, zOffset + 0.0f);
      case ZoopTransition.yNzN:
      case ZoopTransition.zPyP:
        return Quaternion.Euler(xOffset + 90.0f, yOffset + -90f, zOffset + 0.0f);
      case ZoopTransition.yPzN:
      case ZoopTransition.zPyN:
        return Quaternion.Euler(xOffset + -90.0f, yOffset + -90f, zOffset + 0.0f);
      case ZoopTransition.yNzP:
      case ZoopTransition.zNyP:
        return Quaternion.Euler(xOffset + 90.0f, yOffset + 90f, zOffset + 0.0f);
      default:
        // We should normally never get here because GetTransition already validates
        // the input, but identity is a safe fallback if the enum mapping changes.
        return Quaternion.identity;
    }
  }
}
