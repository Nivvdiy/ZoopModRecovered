using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// One direction-run in a planned zoop path.
/// A <c>List&lt;ZoopSegment&gt;</c> is the complete flat path — no nested enumeration needed.
/// Constructed by <see cref="AppendBoundarySegments"/>.
/// </summary>
internal readonly struct ZoopSegment(
  ZoopDirection direction, int count, bool increasing,
  bool isWaypointStart, Vector3 startPos, bool isCorner = false)
{
  /// <summary>World axis being traversed in this run.</summary>
  public ZoopDirection Direction { get; } = direction;

  /// <summary>Total cell count from the path plan (before endpoint deduplication).</summary>
  public int Count { get; } = count;

  /// <summary>Whether the run travels in the positive axis direction.</summary>
  public bool Increasing { get; } = increasing;

  /// <summary>True when this is the first run of a non-first waypoint segment.</summary>
  public bool IsWaypointStart { get; } = isWaypointStart;

  /// <summary>World-space position of the waypoint that starts this run's parent segment.</summary>
  public Vector3 StartPos { get; } = startPos;

  /// <summary>True when this segment represents a direction-change junction cell (corner piece).</summary>
  public bool IsCorner { get; } = isCorner;

  /// <summary>
  /// Appends boundary-aware segments derived from the displacement between
  /// <paramref name="start"/> and <paramref name="end"/>.
  /// The first cell of each run is isolated as a 1-cell segment when it is the path start,
  /// a waypoint junction, or a direction change (corner). <paramref name="prevDirection"/> is
  /// updated to the last emitted direction so the caller can carry it across waypoint loops.
  /// </summary>
  public static void AppendBoundarySegments(
    List<ZoopSegment> segments, Vector3 start, Vector3 end,
    bool isFirstWaypoint, bool isLastWaypoint, ZoopDirection prevDirection)
  {
    var hasX = Math.Abs(end.x - start.x) > float.Epsilon;
    var hasY = Math.Abs(end.y - start.y) > float.Epsilon;
    var hasZ = Math.Abs(end.z - start.z) > float.Epsilon;

    var isFirstSegment = true;
    var axisCount = (hasX ? 1 : 0) + (hasY ? 1 : 0) + (hasZ ? 1 : 0);
    var axisIdx = 0;

    void AddSegment(ZoopDirection dir, int count, bool increasing)
    {
      var waypointStart = !isFirstWaypoint && isFirstSegment;
      var corner = prevDirection != ZoopDirection.none && dir != prevDirection;
      var needsBoundary = segments.Count == 0 || waypointStart || corner;
      var isolateEnd = isLastWaypoint && ++axisIdx == axisCount;

      if (needsBoundary && isolateEnd && count > 2)
      {
        segments.Add(new ZoopSegment(dir, 2, increasing, waypointStart, start, corner));
        segments.Add(new ZoopSegment(dir, count - 1, increasing, isWaypointStart: false, start));
        segments.Add(new ZoopSegment(dir, 1, increasing, isWaypointStart: false, start));
      }
      else if (needsBoundary && count > 1)
      {
        segments.Add(new ZoopSegment(dir, 2, increasing, waypointStart, start, corner));
        if (isolateEnd)
          segments.Add(new ZoopSegment(dir, 1, increasing, isWaypointStart: false, start));
        else if (count > 2)
          segments.Add(new ZoopSegment(dir, count - 1, increasing, isWaypointStart: false, start));
      }
      else if (isolateEnd && count > 1)
      {
        segments.Add(new ZoopSegment(dir, count, increasing, waypointStart, start, corner));
        segments.Add(new ZoopSegment(dir, 1, increasing, isWaypointStart: false, start));
      }
      else
      {
        segments.Add(new ZoopSegment(dir, count, increasing, waypointStart, start, corner));
      }

      prevDirection = dir;
      isFirstSegment = false;
    }

    if (!hasX && !hasY && !hasZ)
    {
      AddSegment(ZoopDirection.x, 1, start.x < end.x);
      return;
    }

    if (hasX) AddSegment(ZoopDirection.x, 1 + (int)(Math.Abs(start.x - end.x) * 2), start.x < end.x);
    if (hasY) AddSegment(ZoopDirection.y, 1 + (int)(Math.Abs(start.y - end.y) * 2), start.y < end.y);
    if (hasZ) AddSegment(ZoopDirection.z, 1 + (int)(Math.Abs(start.z - end.z) * 2), start.z < end.z);
  }
}
