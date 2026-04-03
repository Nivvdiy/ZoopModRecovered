using UnityEngine;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// Per-run context emitted by <see cref="ZoopPathPlanner.WalkSmallGridPath"/> for each
/// direction-run in the planned path. Contains every value needed to drive the inner cell loop
/// so callers never repeat traversal bookkeeping.
/// </summary>
internal readonly struct ZoopPathStep(
  int runIndex, int totalRunCount,
  ZoopSegment run, Vector3 baseOffset,
  int zoopCounter, float value,
  ZoopDirection nextDirection,
  bool increasingFromPrevious)
{
  /// <summary>Flat index of this run in the path.</summary>
  public int RunIndex { get; } = runIndex;

  /// <summary>Total number of runs in the path.</summary>
  public int TotalRunCount { get; } = totalRunCount;

  /// <summary>World axis being traversed in this run.</summary>
  public ZoopDirection Direction { get; } = run.Direction;

  /// <summary>Whether this run travels in the positive axis direction.</summary>
  public bool Increasing { get; } = run.Increasing;

  /// <summary>Number of cell positions in this run after endpoint deduplication.</summary>
  public int ZoopCounter { get; } = zoopCounter;

  /// <summary>World-space step size (positive or negative) per cell along <see cref="Direction"/>.</summary>
  public float Value { get; } = value;

  /// <summary>World-space position of the start of the parent segment (the waypoint origin).</summary>
  public Vector3 StartPos { get; } = run.StartPos;

  /// <summary>
  /// Cumulative world-space offset at the start of this direction run.
  /// Only the component for the axis traversed by an earlier run in the same segment is non-zero;
  /// the travel axis for this run will be zero until the caller applies a per-cell offset.
  /// </summary>
  public Vector3 BaseOffset { get; } = baseOffset;

  /// <summary>
  /// The next direction after this run, or <see cref="ZoopDirection.none"/> if this is the
  /// last run in the entire path. Eliminates the need for manual next-segment lookahead.
  /// </summary>
  public ZoopDirection NextDirection { get; } = nextDirection;

  /// <summary>
  /// The <c>increasing</c> flag of the previous direction run. Used for corner rotation.
  /// Precomputed by the walker so callers never need to look back themselves.
  /// </summary>
  public bool IncreasingFromPrevious { get; } = increasingFromPrevious;

  /// <summary>Returns true if this is the first run of a non-first waypoint segment.</summary>
  public bool IsWaypointStart { get; } = run.IsWaypointStart;

  /// <summary>Returns true if this is the very first run in the path.</summary>
  public bool IsGlobalFirst => RunIndex == 0;

  /// <summary>Returns true if this is the very last run in the path.</summary>
  public bool IsGlobalLast => RunIndex == TotalRunCount - 1;

  /// <summary>
  /// Computes the world-space position at <paramref name="placementIndex"/> cells along this direction.
  /// </summary>
  public Vector3 GetCellPosition(int placementIndex)
  {
    float x = BaseOffset.x, y = BaseOffset.y, z = BaseOffset.z;
    ZoopPathPlanner.SetDirectionalOffset(ref x, ref y, ref z, Direction, placementIndex * Value);
    return StartPos + new Vector3(x, y, z);
  }
}
