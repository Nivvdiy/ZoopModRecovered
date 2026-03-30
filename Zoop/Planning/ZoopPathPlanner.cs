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

    var directions = new List<(float value, ZoopDirection direction, int count, bool increasing)>
    {
      (absX, ZoopDirection.x, 1 + (int)(Math.Abs(startX - endX) / 2), startX < endX),
      (absY, ZoopDirection.y, 1 + (int)(Math.Abs(startY - endY) / 2), startY < endY),
      (absZ, ZoopDirection.z, 1 + (int)(Math.Abs(startZ - endZ) / 2), startZ < endZ)
    };

    directions.Sort((a, b) => b.value.CompareTo(a.value));

    plane.Directions = (direction1: directions[0].direction, direction2: directions[1].direction);
    plane.Count = (direction1: directions[0].count, direction2: directions[1].count);
    plane.Increasing = (direction1: directions[0].increasing, direction2: directions[1].increasing);
  }
}
