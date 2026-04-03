using System;
using System.Collections.Generic;
using UnityEngine;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Planning;

internal static class ZoopPathPlanner
{
  private const float SmallGridCellSpacing = 0.5f;

  private static readonly List<ZoopSegment> _segments = new List<ZoopSegment>();

  public static IReadOnlyList<ZoopSegment> BuildSmallGridPlan(IReadOnlyList<Vector3> waypoints, Vector3 currentPos)
  {
    _segments.Clear();
    var prevDirection = ZoopDirection.none;

    for (var wpIndex = 0; wpIndex < waypoints.Count; wpIndex++)
    {
      var startPos = waypoints[wpIndex];
      var endPos = wpIndex < waypoints.Count - 1 ? waypoints[wpIndex + 1] : currentPos;
      ZoopSegment.AppendBoundarySegments(_segments, startPos, endPos, wpIndex == 0, wpIndex == waypoints.Count - 1, prevDirection);
      prevDirection = _segments[_segments.Count - 1].Direction;
    }

    return _segments;
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

  /// <summary>
  /// Walks every segment in the flat <paramref name="segments"/> list and invokes
  /// <paramref name="onSegment"/> once per segment with full traversal context.
  /// </summary>
  public static void WalkSmallGridPath(
    IReadOnlyList<ZoopSegment> segments,
    Action<ZoopPathStep> onSegment)
  {
    var totalCount = segments.Count;
    var lastDirection = ZoopDirection.none;
    var lastIncreasing = false;
    float xOffset = 0, yOffset = 0, zOffset = 0;

    for (var i = 0; i < totalCount; i++)
    {
      var segment = segments[i];

      // Reset the accumulated offset at the start of each new waypoint span.
      if (i == 0 || segment.IsWaypointStart)
      {
        xOffset = 0; yOffset = 0; zOffset = 0;
      }

      var zoopCounter = GetPlacementCount(i, totalCount, segment.Count);
      var cellStride = segment.Increasing ? SmallGridCellSpacing : -SmallGridCellSpacing;
      var increasingFromPrevious = lastDirection != ZoopDirection.none && lastIncreasing;

      onSegment(new ZoopPathStep(i, segment, new Vector3(xOffset, yOffset, zOffset),
        zoopCounter, cellStride, increasingFromPrevious));

      lastDirection = segment.Direction;
      lastIncreasing = segment.Increasing;
      var dirOffset =
        segment.Direction switch
        {
          ZoopDirection.x => xOffset,
          ZoopDirection.y => yOffset,
          _ => zOffset
        };

      SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, segment.Direction, dirOffset + zoopCounter * cellStride);
    }
  }

  public static int GetPlacementCount(int segmentIndex, int totalSegmentCount, int zoopCount) =>
    segmentIndex < totalSegmentCount - 1 ? zoopCount - 1 : zoopCount;

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
    ZoopDirection dir1, dir2;
    int cnt1, cnt2;
    bool inc1, inc2;

    if (absX >= absY && absX >= absZ)
    {
      // X is largest
      dir1 = ZoopDirection.x; cnt1 = 1 + (int)(Math.Abs(startX - endX) / 2); inc1 = startX < endX;
      if (absY >= absZ)
      {
        dir2 = ZoopDirection.y; cnt2 = 1 + (int)(Math.Abs(startY - endY) / 2); inc2 = startY < endY;
      }
      else
      {
        dir2 = ZoopDirection.z; cnt2 = 1 + (int)(Math.Abs(startZ - endZ) / 2); inc2 = startZ < endZ;
      }
    }
    else if (absY >= absX && absY >= absZ)
    {
      // Y is largest
      dir1 = ZoopDirection.y; cnt1 = 1 + (int)(Math.Abs(startY - endY) / 2); inc1 = startY < endY;
      if (absX >= absZ)
      {
        dir2 = ZoopDirection.x; cnt2 = 1 + (int)(Math.Abs(startX - endX) / 2); inc2 = startX < endX;
      }
      else
      {
        dir2 = ZoopDirection.z; cnt2 = 1 + (int)(Math.Abs(startZ - endZ) / 2); inc2 = startZ < endZ;
      }
    }
    else
    {
      // Z is largest
      dir1 = ZoopDirection.z; cnt1 = 1 + (int)(Math.Abs(startZ - endZ) / 2); inc1 = startZ < endZ;
      if (absX >= absY)
      {
        dir2 = ZoopDirection.x; cnt2 = 1 + (int)(Math.Abs(startX - endX) / 2); inc2 = startX < endX;
      }
      else
      {
        dir2 = ZoopDirection.y; cnt2 = 1 + (int)(Math.Abs(startY - endY) / 2); inc2 = startY < endY;
      }
    }

    plane.Directions = (direction1: dir1, direction2: dir2);
    plane.Count = (direction1: cnt1, direction2: cnt2);
    plane.Increasing = (direction1: inc1, direction2: inc2);
  }
}
