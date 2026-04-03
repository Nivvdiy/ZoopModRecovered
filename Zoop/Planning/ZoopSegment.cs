using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// One direction-run in a planned zoop path.
/// A <c>List&lt;ZoopSegment&gt;</c> is the complete flat path — no nested enumeration needed.
/// Constructed by <see cref="ZoopSegment.AppendRuns"/>.
/// </summary>
internal readonly struct ZoopSegment(
  ZoopDirection direction, int count, bool increasing,
  bool isWaypointStart, Vector3 startPos)
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

  /// <summary>
  /// Appends one <see cref="ZoopSegment"/> per active axis (X→Y→Z order) derived from the
  /// displacement between <paramref name="start"/> and <paramref name="end"/>.
  /// The first run of a non-first waypoint gets <see cref="IsWaypointStart"/> = <see langword="true"/>.
  /// If there is no displacement, a single default x-axis run is added (single-placement fallback).
  /// </summary>
  public static void AppendRuns(List<ZoopSegment> runs, Vector3 start, Vector3 end, bool isFirstWaypoint)
  {
    var hasX = Math.Abs(end.x - start.x) > float.Epsilon;
    var hasY = Math.Abs(end.y - start.y) > float.Epsilon;
    var hasZ = Math.Abs(end.z - start.z) > float.Epsilon;

    if (!hasX && !hasY && !hasZ)
    {
      // Single-placement: add one x-axis run so there is always at least one entry.
      runs.Add(new ZoopSegment(ZoopDirection.x,
        1 + (int)(Math.Abs(start.x - end.x) * 2), start.x < end.x,
        isWaypointStart: !isFirstWaypoint, start));
      return;
    }

    var firstRun = true;

    if (hasX)
    {
      var count = 1 + (int)(Math.Abs(start.x - end.x) * 2);
      var inc = start.x < end.x;
      runs.Add(new ZoopSegment(ZoopDirection.x, count, inc,
        isWaypointStart: !isFirstWaypoint && firstRun, start));
      firstRun = false;
    }

    if (hasY)
    {
      var count = 1 + (int)(Math.Abs(start.y - end.y) * 2);
      var inc = start.y < end.y;
      runs.Add(new ZoopSegment(ZoopDirection.y, count, inc,
        isWaypointStart: !isFirstWaypoint && firstRun, start));
      firstRun = false;
    }

    if (hasZ)
    {
      var count = 1 + (int)(Math.Abs(start.z - end.z) * 2);
      var inc = start.z < end.z;
      runs.Add(new ZoopSegment(ZoopDirection.z, count, inc,
        isWaypointStart: !isFirstWaypoint && firstRun, start));
    }
  }
}
