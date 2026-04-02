using System;

namespace ZoopMod.Zoop.Planning;

/// <summary>
/// Per-axis traversal data for one segment of a planned zoop path.
/// </summary>
internal readonly struct ZoopAxisData(int count, bool increasing)
{
  public int Count { get; } = count;
  public bool Increasing { get; } = increasing;
}

/// <summary>
/// One segment of a planned zoop path between two waypoints.
/// Axis data is stored in per-axis structs; direction order is stored in three inline fields
/// rather than a <c>List&lt;ZoopDirection&gt;</c> to avoid per-frame heap allocation.
/// </summary>
internal sealed class ZoopSegment
{
  public ZoopAxisData X { get; set; }
  public ZoopAxisData Y { get; set; }
  public ZoopAxisData Z { get; set; }

  private ZoopDirection _dir0;
  private ZoopDirection _dir1;
  private ZoopDirection _dir2;
  private int _dirCount;

  public int DirectionCount => _dirCount;

  public ZoopDirection GetDirection(int index) => index switch
  {
    0 => _dir0,
    1 => _dir1,
    2 => _dir2,
    _ => throw new ArgumentOutOfRangeException(nameof(index), index, null)
  };

  /// <summary>The last direction added, or <see cref="ZoopDirection.none"/> if no directions have been added.</summary>
  public ZoopDirection LastDirection => _dirCount > 0 ? GetDirection(_dirCount - 1) : ZoopDirection.none;

  public void AddDirection(ZoopDirection dir)
  {
    switch (_dirCount)
    {
      case 0: _dir0 = dir; break;
      case 1: _dir1 = dir; break;
      case 2: _dir2 = dir; break;
      default: throw new InvalidOperationException("ZoopSegment supports at most 3 directions.");
    }
    _dirCount++;
  }

  public void ClearDirections() => _dirCount = 0;

  /// <summary>Returns the axis data for the given direction.</summary>
  public ZoopAxisData GetAxis(ZoopDirection dir) => dir switch
  {
    ZoopDirection.x => X,
    ZoopDirection.y => Y,
    ZoopDirection.z => Z,
    _ => throw new ArgumentOutOfRangeException(nameof(dir), dir, null)
  };
}
