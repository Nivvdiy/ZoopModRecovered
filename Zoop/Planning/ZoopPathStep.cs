using UnityEngine;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// Per-segment context emitted by <see cref="ZoopPathPlanner.WalkSmallGridPath"/> for each
/// direction segment in the planned path. Contains every value needed to drive the inner cell loop
/// so callers never repeat traversal bookkeeping.
/// </summary>
internal readonly struct ZoopPathStep(
  int segmentIndex,
  int totalSegmentCount,
  ZoopSegment segment,
  Vector3 baseOffset,
  int zoopCounter,
  float cellStride,
  bool increasingFromPrevious)
{

  public ZoopSegment Segment => segment;

/// <summary>Flat index of this segment in the path.</summary>
  public int SegmentIndex { get; } = segmentIndex;

  /// <summary>Total number of segments in the path.</summary>
  public int TotalSegmentCount { get; } = totalSegmentCount;

  /// <summary>Number of cell positions in this segment after endpoint deduplication.</summary>
  public int ZoopCounter { get; } = zoopCounter;

  /// <summary>World-space step size (positive or negative) per cell along <see cref="Direction"/>.</summary>
  public float CellStride { get; } = cellStride;

  /// <summary>
  /// Cumulative world-space offset at the start of this segment.
  /// Only the component for the axis traversed by an earlier segment in the same waypoint span is non-zero;
  /// the travel axis for this segment will be zero until the caller applies a per-cell offset.
  /// </summary>
  public Vector3 BaseOffset { get; } = baseOffset;

  /// <summary>
  /// The <c>increasing</c> flag of the previous segment. Used for corner rotation.
  /// Precomputed by the walker so callers never need to look back themselves.
  /// </summary>
  public bool IncreasingFromPrevious { get; } = increasingFromPrevious;

  /// <summary>Returns true if this is the very first segment in the path.</summary>
  public bool IsGlobalFirst => SegmentIndex == 0;

  /// <summary>Returns true if this is the very last segment in the path.</summary>
  public bool IsGlobalLast => SegmentIndex == TotalSegmentCount - 1;

  /// <summary>
  /// Computes the world-space position at <paramref name="placementIndex"/> cells along this direction.
  /// </summary>
  public Vector3 GetCellPosition(int placementIndex)
  {
    var x = BaseOffset.x;
    var y = BaseOffset.y;
    var z = BaseOffset.z;

    var dirBase = segment.Direction switch
    {
      ZoopDirection.x => x,
      ZoopDirection.y => y,
      _ => z
    };
    ZoopPathPlanner.SetDirectionalOffset(ref x, ref y, ref z, segment.Direction, dirBase + placementIndex * CellStride);
    return segment.StartPos + new Vector3(x, y, z);
  }
}
