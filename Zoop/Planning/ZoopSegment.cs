using System;
using UnityEngine;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// One ordered direction-run within a <see cref="ZoopSegment"/>: combines the axis, cell count,
/// and travel direction so both lookups previously needed (GetDirection + GetAxis) collapse to one.
/// </summary>
internal readonly struct ZoopSegmentDirection(ZoopDirection direction, int count, bool increasing)
{
  public ZoopDirection Direction { get; } = direction;
  public int Count { get; } = count;
  public bool Increasing { get; } = increasing;
}

/// <summary>
/// One segment of a planned zoop path between two waypoints.
/// Iterate over all direction-runs with <c>foreach (var run in segment)</c>.
/// Constructed via <see cref="FromEndpoints"/> or <see cref="SinglePlacement"/>.
/// </summary>
internal readonly struct ZoopSegment
{
  private readonly ZoopSegmentDirection[] _runs;

  private ZoopSegment(ZoopSegmentDirection[] runs) => _runs = runs;

  /// <summary>Number of direction-runs in this segment.</summary>
  public int Count => _runs?.Length ?? 0;

  /// <summary>Returns the run at <paramref name="index"/>.</summary>
  public ZoopSegmentDirection this[int index] => _runs[index];

  /// <summary>The last run, or a default value if the segment is empty.</summary>
  public ZoopSegmentDirection LastRun => _runs is { Length: > 0 } r ? r[r.Length - 1] : default;

  /// <summary>Returns an enumerator so <c>foreach (var run in segment)</c> works without allocation.</summary>
  public Enumerator GetEnumerator() => new(_runs);

  /// <summary>Struct enumerator for <see cref="ZoopSegment"/> — no heap allocation on foreach.</summary>
  public struct Enumerator
  {
    private readonly ZoopSegmentDirection[] _runs;
    private int _index;

    internal Enumerator(ZoopSegmentDirection[] runs)
    {
      _runs = runs ?? Array.Empty<ZoopSegmentDirection>();
      _index = -1;
    }

    public bool MoveNext() => ++_index < _runs.Length;
    public ZoopSegmentDirection Current => _runs[_index];
  }

  /// <summary>
  /// Calculates a segment from two waypoint positions.
  /// Each axis with non-trivial displacement becomes one direction-run, in X→Y→Z order.
  /// </summary>
  public static ZoopSegment FromEndpoints(Vector3 start, Vector3 end)
  {
    var hasX = Math.Abs(end.x - start.x) > float.Epsilon;
    var hasY = Math.Abs(end.y - start.y) > float.Epsilon;
    var hasZ = Math.Abs(end.z - start.z) > float.Epsilon;
    var count = (hasX ? 1 : 0) + (hasY ? 1 : 0) + (hasZ ? 1 : 0);

    if (count == 0)
      return SinglePlacement(start.x, end.x);

    var runs = new ZoopSegmentDirection[count];
    var i = 0;
    if (hasX) runs[i++] = new ZoopSegmentDirection(ZoopDirection.x, 1 + (int)(Math.Abs(start.x - end.x) * 2), start.x < end.x);
    if (hasY) runs[i++] = new ZoopSegmentDirection(ZoopDirection.y, 1 + (int)(Math.Abs(start.y - end.y) * 2), start.y < end.y);
    if (hasZ) runs[i++] = new ZoopSegmentDirection(ZoopDirection.z, 1 + (int)(Math.Abs(start.z - end.z) * 2), start.z < end.z);
    return new ZoopSegment(runs);
  }

  /// <summary>
  /// Returns a single-cell segment used when start and end are the same position.
  /// </summary>
  public static ZoopSegment SinglePlacement(float startX, float endX) =>
    new(new[] { new ZoopSegmentDirection(ZoopDirection.x, 1 + (int)(Math.Abs(startX - endX) * 2), startX < endX) });
}
