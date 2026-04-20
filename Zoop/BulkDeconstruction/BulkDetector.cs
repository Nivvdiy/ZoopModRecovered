using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Detects and explores connected structures for bulk operations.
/// Uses the game's native SmallGrid.Connected() API for efficient traversal.
/// </summary>
public class BulkDetector
{
  /// <summary>
  /// Explores the entire group of connected structures.
  /// </summary>
  /// <param name="startStructure">The structure to start exploration from</param>
  /// <returns>List of all connected structures in the bulk</returns>
  public List<Structure> ExploreBulk(Structure startStructure)
  {
    if (startStructure == null)
      return new List<Structure>();

    HashSet<Structure> visited = new HashSet<Structure>();
    List<Structure> result = new List<Structure>();
    BulkType bulkType = GetBulkType(startStructure);

    ExploreRecursive(startStructure, bulkType, visited, result);

    return result;
  }

  private void ExploreRecursive(Structure current, BulkType bulkType, HashSet<Structure> visited, List<Structure> result)
  {
    if (current == null || visited.Contains(current))
      return;

    visited.Add(current);
    result.Add(current);

    // Get direct neighbors using game's built-in Connected() method
    if (!(current is SmallGrid smallGrid))
      return;

    List<SmallGrid> neighbors = smallGrid.Connected();
    if (neighbors == null || neighbors.Count == 0)
      return;

    // Create a copy to avoid collection modification during recursion
    List<SmallGrid> neighborsCopy = new List<SmallGrid>(neighbors);

    // Recursively explore each neighbor of the same bulk type
    foreach (SmallGrid neighbor in neighborsCopy)
    {
      if (neighbor == null || visited.Contains(neighbor))
        continue;

      BulkType neighborType = GetBulkType(neighbor);

      // Only explore if same bulk type (cables with cables, pipes with pipes, etc.)
      if (neighborType == bulkType)
      {
        ExploreRecursive(neighbor, bulkType, visited, result);
      }
    }
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
