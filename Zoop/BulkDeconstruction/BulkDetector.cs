using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Detects and explores connected structures for bulk operations.
/// Uses iterative approach with Stack to avoid stack overflow on large networks.
/// </summary>
public class BulkDetector
{
  // Optimization: reusable collections to avoid allocations
  private readonly HashSet<Structure> _visitedPool = new HashSet<Structure>();
  private readonly List<Structure> _resultPool = new List<Structure>();
  private readonly Stack<Structure> _toVisitPool = new Stack<Structure>();

  /// <summary>
  /// Explores the entire group of connected structures.
  /// </summary>
  /// <param name="startStructure">The structure to start exploration from</param>
  /// <returns>List of all connected structures in the bulk</returns>
  public List<Structure> ExploreBulk(Structure startStructure)
  {
    if (startStructure == null)
      return new List<Structure>();

    // Clear and reuse pooled collections
    _visitedPool.Clear();
    _resultPool.Clear();
    _toVisitPool.Clear();

    BulkType bulkType = GetBulkType(startStructure);

    // Iterative exploration using explicit stack (avoids recursion stack overflow)
    _toVisitPool.Push(startStructure);

    while (_toVisitPool.Count > 0)
    {
      Structure current = _toVisitPool.Pop();

      if (current == null || _visitedPool.Contains(current))
        continue;

      _visitedPool.Add(current);
      _resultPool.Add(current);

      // Get direct neighbors using game's built-in Connected() method
      if (!(current is SmallGrid smallGrid))
        continue;

      List<SmallGrid> neighbors = smallGrid.Connected();
      if (neighbors == null || neighbors.Count == 0)
        continue;

      // Thread-safety: iterate over count instead of foreach to avoid enumeration issues
      // The neighbors list may be modified by the game's network tick on another thread
      int neighborCount = neighbors.Count;
      for (int i = 0; i < neighborCount; i++)
      {
        // Bounds check in case list was modified
        if (i >= neighbors.Count)
          break;

        SmallGrid neighbor = neighbors[i];
        if (neighbor == null || _visitedPool.Contains(neighbor))
          continue;

        BulkType neighborType = GetBulkType(neighbor);

        // Only explore if same bulk type (cables with cables, pipes with pipes, etc.)
        if (neighborType == bulkType)
        {
          _toVisitPool.Push(neighbor);
        }
      }
    }

    // Return a copy to avoid external modification of pooled list
    return new List<Structure>(_resultPool);
  }

  private BulkType GetBulkType(Structure structure)
  {
    if (structure is Cable)
      return BulkType.Cable;
    if (structure is Pipe)
      return BulkType.Pipe;
    if (structure is Chute)
      return BulkType.Chute;

    return BulkType.Unknown;
  }
}

/// <summary>
/// Type of bulk element.
/// </summary>
public enum BulkType
{
  Unknown,
  Cable,
  Pipe,
  Chute
}
