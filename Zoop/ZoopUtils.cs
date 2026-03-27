using System;
using UnityEngine;

namespace ZoopMod.Zoop
{
  public static class ZoopUtils
  {
    public static ZoopTransition GetTransition(ZoopDirection zoopDirectionFrom, bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo)
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

    public static Quaternion GetCornerRotation(ZoopDirection zoopDirectionFrom, bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo, float xOffset, float yOffset, float zOffset)
    {
      switch (GetTransition(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo))
      {
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
          return Quaternion.identity;
      }
    }
  }
}
