using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZoopMod.Zoop.Planning.SmallGrid;

/// <summary>
/// One direction-run in a planned zoop path.
/// A <c>List&lt;ZoopSegment&gt;</c> is the complete flat path — no nested enumeration needed.
/// Constructed by <see cref="AppendBoundarySegments"/>.
/// </summary>
internal readonly struct ZoopSegment(
  ZoopDirection direction,
  int length,
  bool increasing,
  bool isWaypointStart,
  Vector3 startPos,
  bool isCorner = false)
{
  /// <summary>World axis being traversed in this run.</summary>
  public ZoopDirection Direction { get; } = direction;

  /// <summary>Total cell count from the path plan (before endpoint deduplication).</summary>
  public int Length { get; } = length;

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

    var segmentBuilder = new SegmentBuilder(segments, start, end, isFirstWaypoint, isLastWaypoint, prevDirection);

    // Handle Single Piece segments
    if (!hasX && !hasY && !hasZ)
    {
      segmentBuilder.Add(ZoopDirection.x);
      return;
    }

    if (hasX)
      segmentBuilder.Add(ZoopDirection.x);
    if (hasY)
      segmentBuilder.Add(ZoopDirection.y);
    if (hasZ)
      segmentBuilder.Add(ZoopDirection.z);
  }

  private ref struct SegmentBuilder(
    List<ZoopSegment> segments,
    Vector3 start,
    Vector3 end,
    bool isFirstWaypoint,
    bool isLastWaypoint,
    ZoopDirection prevDirection)
  {
    private bool _isFirstSegment = true;

    public void Add(ZoopDirection dir)
    {
      var (axisStart, axisEnd) = dir switch
      {
        ZoopDirection.x => (start.x, end.x),
        ZoopDirection.y => (start.y, end.y),
        _ => (start.z, end.z)
      };

      var length = 1 + (int)(Math.Abs(axisStart - axisEnd) * 2);
      var increasing = axisStart < axisEnd;
      var isLastAxis = dir switch
      {
        ZoopDirection.x => Math.Abs(end.y - start.y) <= float.Epsilon && Math.Abs(end.z - start.z) <= float.Epsilon,
        ZoopDirection.y => Math.Abs(end.z - start.z) <= float.Epsilon,
        _ => true
      };

      var waypointStart = !isFirstWaypoint && _isFirstSegment;
      var corner = prevDirection != ZoopDirection.none && dir != prevDirection;
      var needsBoundary = segments.Count == 0 || waypointStart || corner;
      AppendAxisSegments(dir, length, increasing, needsBoundary, isLastAxis && isLastWaypoint, waypointStart, corner);
      prevDirection = dir;
      _isFirstSegment = false;
    }

    private readonly void AppendAxisSegments(
      ZoopDirection dir,
      int length,
      bool increasing,
      bool needsBoundary,
      bool isolateEnd,
      bool waypointStart,
      bool corner)
    {
      switch (needsBoundary)
      {
        case true when isolateEnd && length > 2:
          segments.Add(new ZoopSegment(dir, 2, increasing, waypointStart, start, corner));
          segments.Add(new ZoopSegment(dir, length - 1, increasing, isWaypointStart: false, start));
          segments.Add(new ZoopSegment(dir, 1, increasing, isWaypointStart: false, start));
          break;
        case true when length > 1:
        {
          segments.Add(new ZoopSegment(dir, 2, increasing, waypointStart, start, corner));
          if (isolateEnd)
            segments.Add(new ZoopSegment(dir, 1, increasing, isWaypointStart: false, start));
          else if (length > 2)
            segments.Add(new ZoopSegment(dir, length - 1, increasing, isWaypointStart: false, start));
          break;
        }
        // case false:
        default:
          {
            if (isolateEnd && length > 1)
            {
              segments.Add(new ZoopSegment(dir, length, increasing, waypointStart, start, corner));
              segments.Add(new ZoopSegment(dir, 1, increasing, isWaypointStart: false, start));
            }
            else
            {
              segments.Add(new ZoopSegment(dir, length, increasing, waypointStart, start, corner));
            }

            break;
          }
      }
    }
  }
}
