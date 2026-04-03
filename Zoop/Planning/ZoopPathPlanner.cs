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
    var increasingFromPrevious = false;
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

      onSegment(new ZoopPathStep(i, segment, new Vector3(xOffset, yOffset, zOffset),
        zoopCounter, cellStride, increasingFromPrevious));

      increasingFromPrevious = segment.Increasing;

      var basedir = GetDirectionalComponent(new Vector3(xOffset, yOffset, zOffset), segment.Direction);
      SetDirectionalOffset(ref xOffset, ref yOffset, ref zOffset, segment.Direction,
        basedir + zoopCounter * cellStride);
    }
  }

  public static int GetPlacementCount(int segmentIndex, int totalSegmentCount, int zoopCount) =>
    segmentIndex < totalSegmentCount - 1 ? zoopCount - 1 : zoopCount;

  public static float GetDirectionalComponent(Vector3 v, ZoopDirection direction) =>
    direction switch
    {
      ZoopDirection.x => v.x,
      ZoopDirection.y => v.y,
      _ => v.z
    };

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
    var absX = Math.Abs(endPos.x - startPos.x) / 2;
    var absY = Math.Abs(endPos.y - startPos.y) / 2;
    var absZ = Math.Abs(endPos.z - startPos.z) / 2;

    // Pick the two largest axes without allocating a list — only three candidates exist so
    // two comparisons are sufficient to identify the top-2 in descending order.
    ZoopDirection dir1, dir2;
    int cnt1, cnt2;
    bool inc1, inc2;

    if (absX >= absY && absX >= absZ)
    {
      // X is largest
      dir1 = ZoopDirection.x; cnt1 = 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2); inc1 = startPos.x < endPos.x;
      if (absY >= absZ)
      {
        dir2 = ZoopDirection.y; cnt2 = 1 + (int)(Math.Abs(startPos.y - endPos.y) / 2); inc2 = startPos.y < endPos.y;
      }
      else
      {
        dir2 = ZoopDirection.z; cnt2 = 1 + (int)(Math.Abs(startPos.z - endPos.z) / 2); inc2 = startPos.z < endPos.z;
      }
    }
    else if (absY >= absX && absY >= absZ)
    {
      // Y is largest
      dir1 = ZoopDirection.y; cnt1 = 1 + (int)(Math.Abs(startPos.y - endPos.y) / 2); inc1 = startPos.y < endPos.y;
      if (absX >= absZ)
      {
        dir2 = ZoopDirection.x; cnt2 = 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2); inc2 = startPos.x < endPos.x;
      }
      else
      {
        dir2 = ZoopDirection.z; cnt2 = 1 + (int)(Math.Abs(startPos.z - endPos.z) / 2); inc2 = startPos.z < endPos.z;
      }
    }
    else
    {
      // Z is largest
      dir1 = ZoopDirection.z; cnt1 = 1 + (int)(Math.Abs(startPos.z - endPos.z) / 2); inc1 = startPos.z < endPos.z;
      if (absX >= absY)
      {
        dir2 = ZoopDirection.x; cnt2 = 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2); inc2 = startPos.x < endPos.x;
      }
      else
      {
        dir2 = ZoopDirection.y; cnt2 = 1 + (int)(Math.Abs(startPos.y - endPos.y) / 2); inc2 = startPos.y < endPos.y;
      }
    }

    plane.Directions = (direction1: dir1, direction2: dir2);
    plane.Count = (direction1: cnt1, direction2: cnt2);
    plane.Increasing = (direction1: inc1, direction2: inc2);
  }
}
