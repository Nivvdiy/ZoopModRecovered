using UnityEngine;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// Per-direction context emitted by <see cref="ZoopPathPlanner.WalkSmallGridPath"/> for each
/// direction-run in the planned path. Contains every value needed to drive the inner cell loop
/// so callers never repeat the outer traversal bookkeeping.
/// </summary>
internal readonly struct ZoopPathStep(
  int segmentIndex, int directionIndex,
  int totalSegmentCount, int directionCount,
  ZoopDirection direction, ZoopAxisData axis,
  int zoopCounter, float value,
  Vector3 startPos, Vector3 baseOffset,
  ZoopDirection nextDirection,
  bool increasingFromPrevious)
{
  /// <summary>Index of the current segment (waypoint pair) in the path.</summary>
  public int SegmentIndex { get; } = segmentIndex;

  /// <summary>Index of the current direction within the current segment.</summary>
  public int DirectionIndex { get; } = directionIndex;

  /// <summary>Total number of segments in the path.</summary>
  public int TotalSegmentCount { get; } = totalSegmentCount;

  /// <summary>Total number of directions in the current segment.</summary>
  public int DirectionCount { get; } = directionCount;

  /// <summary>World axis being traversed in this run.</summary>
  public ZoopDirection Direction { get; } = direction;

  /// <summary>Count and increasing flag for this direction run.</summary>
  public ZoopAxisData Axis { get; } = axis;

  /// <summary>Number of cell positions in this run after endpoint deduplication.</summary>
  public int ZoopCounter { get; } = zoopCounter;

  /// <summary>World-space step size (positive or negative) per cell along <see cref="Direction"/>.</summary>
  public float Value { get; } = value;

  /// <summary>World-space position of the start of the parent segment (the waypoint origin).</summary>
  public Vector3 StartPos { get; } = startPos;

  /// <summary>
  /// Cumulative world-space offset at the start of this direction run.
  /// Only the component for the axis traversed by an earlier direction in the same segment is non-zero;
  /// the travel axis for this run will be zero until the caller applies a per-cell offset.
  /// </summary>
  public Vector3 BaseOffset { get; } = baseOffset;

  /// <summary>
  /// The next direction after this run, or <see cref="ZoopDirection.none"/> if this is the
  /// last run in the entire path. Eliminates the need for manual next-segment lookahead.
  /// </summary>
  public ZoopDirection NextDirection { get; } = nextDirection;

  /// <summary>
  /// The <c>increasing</c> flag of the previous direction run (i.e. the direction the zoop was
  /// travelling before it arrived at this run). Used for corner rotation. Precomputed by the
  /// walker so callers never need to look up the previous segment themselves.
  /// </summary>
  public bool IncreasingFromPrevious { get; } = increasingFromPrevious;

  /// <summary>Returns true if this is the very first direction-run in the entire path.</summary>
  public bool IsGlobalFirst => SegmentIndex == 0 && DirectionIndex == 0;

  /// <summary>Returns true if this is the very last direction-run in the entire path.</summary>
  public bool IsGlobalLast => SegmentIndex == TotalSegmentCount - 1 && DirectionIndex == DirectionCount - 1;

  /// <summary>Returns true if this is the first direction of a non-first waypoint segment.</summary>
  public bool IsWaypointStart => SegmentIndex > 0 && DirectionIndex == 0;

  /// <summary>
  /// Computes the world-space position at <paramref name="placementIndex"/> cells along this direction,
  /// starting from <see cref="BaseOffset"/> and advancing one step per cell.
  /// </summary>
  public Vector3 GetCellPosition(int placementIndex)
  {
    float x = BaseOffset.x, y = BaseOffset.y, z = BaseOffset.z;
    ZoopPathPlanner.SetDirectionalOffset(ref x, ref y, ref z, Direction, placementIndex * Value);
    return StartPos + new Vector3(x, y, z);
  }
}
