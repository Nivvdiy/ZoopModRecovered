using System;
using System.Collections.Generic;
using UnityEngine;
using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Planning.SmallGrid;

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

      var zoopCounter = GetPlacementCount(i, totalCount, segment.Length);
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

  public static Vector3 WithDirectionalOffset(Vector3 v, ZoopDirection direction, float value) =>
    direction switch
    {
      ZoopDirection.x => new Vector3(value, v.y, v.z),
      ZoopDirection.y => new Vector3(v.x, value, v.z),
      _ => new Vector3(v.x, v.y, value)
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

    // Pick the two largest axes without allocating a list.
    // Represent each axis as a tuple so they can be reordered by displacement.
    var ax = (dir: ZoopDirection.x, abs: absX, s: startPos.x, e: endPos.x);
    var ay = (dir: ZoopDirection.y, abs: absY, s: startPos.y, e: endPos.y);
    var az = (dir: ZoopDirection.z, abs: absZ, s: startPos.z, e: endPos.z);

    // Sort descending — 2 comparisons suffice to find the top-2 of 3 elements.
    // After sorting, ax is the longest axis (primary) and ay is the second longest (secondary).
    if (ay.abs > ax.abs) (ax, ay) = (ay, ax);
    if (az.abs > ax.abs) (ax, az) = (az, ax);
    if (az.abs > ay.abs) (ay, _) = (az, ay);

    var (cnt1, inc1) = AxisMetrics(ax.s, ax.e);
    var (cnt2, inc2) = AxisMetrics(ay.s, ay.e);

    plane.Directions = (direction1: ax.dir, direction2: ay.dir);
    plane.Count = (direction1: cnt1, direction2: cnt2);
    plane.Increasing = (direction1: inc1, direction2: inc2);
  }


  // Returns the cell count and travel direction for a single axis given start/end coordinates.
  // cnt = number of small-grid cells covering the distance (cell spacing is 0.5, so distance / 0.5 = distance * 2).
  // inc = true when the axis travels in the positive direction.
  private static (int cnt, bool inc) AxisMetrics(float s, float e)
  {
    return (1 + (int)(Math.Abs(s - e) / 2), s < e);
  }
}
