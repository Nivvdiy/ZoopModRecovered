using System;
using System.Collections.Generic;
using UnityEngine;
using ZoopMod.Zoop.Planning;

namespace ZoopMod.Zoop.Planning.SmallGrid;

internal static class ZoopPathPlanner
{
  private const float SmallGridCellSpacing = 0.5f;

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
}
