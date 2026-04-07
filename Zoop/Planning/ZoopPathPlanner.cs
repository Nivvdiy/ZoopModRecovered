using System;
using System.Collections.Generic;
using UnityEngine;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Planning;

internal static class ZoopPathPlanner
{
  /// <summary>
  /// Populates <paramref name="segments"/> in-place (clearing it first) and returns whether the path
  /// covers a single placement position. The caller owns the list and should reuse it across calls
  /// to avoid per-frame allocation.
  /// </summary>
  public static bool BuildSmallGridPlan(IReadOnlyList<Vector3> waypoints, Vector3 currentPos,
    List<ZoopSegment> segments)
  {
    segments.Clear();
    var isSinglePlacement = true;

    for (var wpIndex = 0; wpIndex < waypoints.Count; wpIndex++)
    {
      var startPos = waypoints[wpIndex];
      var endPos = wpIndex < waypoints.Count - 1 ? waypoints[wpIndex + 1] : currentPos;

      var segment = new ZoopSegment();
      CalculateZoopSegments(startPos, endPos, segment);

      isSinglePlacement = ZoopPositionUtility.IsSameZoopPosition(startPos, endPos);
      if (isSinglePlacement)
      {
        segment.CountX = 1 + (int)(Math.Abs(startPos.x - endPos.x) * 2);
        segment.IncreasingX = startPos.x < endPos.x;
        segment.Directions.Add(ZoopDirection.x);
      }

      segments.Add(segment);
    }

    return isSinglePlacement;
  }

  public static ZoopPlane BuildBigGridPlane(Vector3 startPos, Vector3 endPos)
  {
    var plane = new ZoopPlane();
    CalculateZoopPlane(startPos, endPos, plane);

    if (!ZoopPositionUtility.IsSameZoopPosition(startPos, endPos))
    {
      return plane;
    }

    plane.Count = (direction1: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2),
      direction2: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2));
    plane.Increasing = (direction1: startPos.x < endPos.x, direction2: startPos.y < endPos.y);
    plane.Directions = (direction1: ZoopDirection.x, direction2: ZoopDirection.y);
    return plane;
  }

  public static float GetDirectionalPlacementValue(bool increasing, bool isSmallGridCursor, int spacing)
  {
    var safeSpacing = Mathf.Max(spacing, 1);
    var minValue = isSmallGridCursor ? 0.5f : 2f;
    return increasing ? minValue * safeSpacing : -(minValue * safeSpacing);
  }

  public static bool GetIncreasingFromPreviousDirection(List<ZoopSegment> segments, ZoopSegment segment,
    int segmentIndex,
    int directionIndex, int placementIndex, ZoopDirection lastDirection)
  {
    var increasingFrom = lastDirection != ZoopDirection.none &&
                         GetIncreasingForDirection(lastDirection, segment);
    if (segmentIndex <= 0 || directionIndex != 0 || placementIndex != 0)
    {
      return increasingFrom;
    }

    var lastSegment = segments[segmentIndex - 1];
    return GetIncreasingForDirection(lastSegment.Directions[lastSegment.Directions.Count - 1], lastSegment);
  }

  public static int GetCountForDirection(ZoopDirection direction, ZoopSegment segment)
  {
    return direction switch
    {
      ZoopDirection.x => segment.CountX,
      ZoopDirection.y => segment.CountY,
      ZoopDirection.z => segment.CountZ,
      _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };
  }

  public static bool GetIncreasingForDirection(ZoopDirection direction, ZoopSegment segment)
  {
    return direction switch
    {
      ZoopDirection.x => segment.IncreasingX,
      ZoopDirection.y => segment.IncreasingY,
      ZoopDirection.z => segment.IncreasingZ,
      _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };
  }

  public static int GetPlacementCount(int segmentCount, int segmentIndex, int directionCount, int directionIndex,
    int zoopCount)
  {
    if ((segmentIndex < segmentCount - 1 && directionIndex == directionCount - 1) ||
        directionIndex < directionCount - 1)
    {
      return zoopCount - 1;
    }

    return zoopCount;
  }

  public static void SetDirectionalOffset(ref float xOffset, ref float yOffset, ref float zOffset,
    ZoopDirection direction,
    float value)
  {
    switch (direction)
    {
      case ZoopDirection.x:
        xOffset = value;
        return;
      case ZoopDirection.y:
        yOffset = value;
        return;
      case ZoopDirection.z:
        zOffset = value;
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
    }
  }

  private static void CalculateZoopSegments(Vector3 startPos, Vector3 endPos, ZoopSegment segment)
  {
    segment.Directions.Clear();

    var startX = startPos.x;
    var startY = startPos.y;
    var startZ = startPos.z;
    var endX = endPos.x;
    var endY = endPos.y;
    var endZ = endPos.z;

    var absX = Math.Abs(endX - startX);
    var absY = Math.Abs(endY - startY);
    var absZ = Math.Abs(endZ - startZ);

    if (absX > float.Epsilon)
    {
      segment.CountX = 1 + (int)(Math.Abs(startX - endX) * 2);
      segment.IncreasingX = startX < endX;
      segment.Directions.Add(ZoopDirection.x);
    }

    if (absY > float.Epsilon)
    {
      segment.CountY = 1 + (int)(Math.Abs(startY - endY) * 2);
      segment.IncreasingY = startY < endY;
      segment.Directions.Add(ZoopDirection.y);
    }

    if (absZ > float.Epsilon)
    {
      segment.CountZ = 1 + (int)(Math.Abs(startZ - endZ) * 2);
      segment.IncreasingZ = startZ < endZ;
      segment.Directions.Add(ZoopDirection.z);
    }
  }

  private static void CalculateZoopPlane(Vector3 startPos, Vector3 endPos, ZoopPlane plane)
  {
    var startX = startPos.x;
    var startY = startPos.y;
    var startZ = startPos.z;
    var endX = endPos.x;
    var endY = endPos.y;
    var endZ = endPos.z;

    var absX = Math.Abs(endX - startX) / 2;
    var absY = Math.Abs(endY - startY) / 2;
    var absZ = Math.Abs(endZ - startZ) / 2;

    // Pick the two largest axes without allocating a list — only three candidates exist so
    // two comparisons are sufficient to identify the top-2 in descending order.
    float val1, val2;
    ZoopDirection dir1, dir2;
    int cnt1, cnt2;
    bool inc1, inc2;

    if (absX >= absY && absX >= absZ)
    {
      // X is largest
      val1 = absX; dir1 = ZoopDirection.x; cnt1 = 1 + (int)(Math.Abs(startX - endX) / 2); inc1 = startX < endX;
      if (absY >= absZ)
      {
        val2 = absY; dir2 = ZoopDirection.y; cnt2 = 1 + (int)(Math.Abs(startY - endY) / 2); inc2 = startY < endY;
      }
      else
      {
        val2 = absZ; dir2 = ZoopDirection.z; cnt2 = 1 + (int)(Math.Abs(startZ - endZ) / 2); inc2 = startZ < endZ;
      }
    }
    else if (absY >= absX && absY >= absZ)
    {
      // Y is largest
      val1 = absY; dir1 = ZoopDirection.y; cnt1 = 1 + (int)(Math.Abs(startY - endY) / 2); inc1 = startY < endY;
      if (absX >= absZ)
      {
        val2 = absX; dir2 = ZoopDirection.x; cnt2 = 1 + (int)(Math.Abs(startX - endX) / 2); inc2 = startX < endX;
      }
      else
      {
        val2 = absZ; dir2 = ZoopDirection.z; cnt2 = 1 + (int)(Math.Abs(startZ - endZ) / 2); inc2 = startZ < endZ;
      }
    }
    else
    {
      // Z is largest
      val1 = absZ; dir1 = ZoopDirection.z; cnt1 = 1 + (int)(Math.Abs(startZ - endZ) / 2); inc1 = startZ < endZ;
      if (absX >= absY)
      {
        val2 = absX; dir2 = ZoopDirection.x; cnt2 = 1 + (int)(Math.Abs(startX - endX) / 2); inc2 = startX < endX;
      }
      else
      {
        val2 = absY; dir2 = ZoopDirection.y; cnt2 = 1 + (int)(Math.Abs(startY - endY) / 2); inc2 = startY < endY;
      }
    }

    // Suppress unused-variable warnings for val1/val2 — they are not stored on the plane but are
    // kept to make the parallel structure of each branch clear and easy to verify.
    _ = val1;
    _ = val2;

    plane.Directions = (direction1: dir1, direction2: dir2);
    plane.Count = (direction1: cnt1, direction2: cnt2);
    plane.Increasing = (direction1: inc1, direction2: inc2);
  }
}
