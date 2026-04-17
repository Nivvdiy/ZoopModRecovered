using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Detects and explores connected network structures.
/// Uses the game's native SmallGrid.Connected() API for efficient traversal.
/// </summary>
public class NetworkDetector
{
  /// <summary>
  /// Explores the entire network connected to the given structure.
  /// </summary>
  /// <param name="startStructure">The structure to start exploration from</param>
  /// <returns>List of all connected structures in the network</returns>
  public List<Structure> ExploreNetwork(Structure startStructure)
  {
    if (startStructure == null)
      return new List<Structure>();

    var visited = new HashSet<Structure>();
    var result = new List<Structure>();
    var networkType = GetNetworkType(startStructure);

    ExploreRecursive(startStructure, networkType, visited, result);

    return result;
  }

  private void ExploreRecursive(Structure current, NetworkType networkType, HashSet<Structure> visited, List<Structure> result)
  {
    if (current == null || visited.Contains(current))
      return;

    visited.Add(current);
    result.Add(current);

    // Get direct neighbors using game's built-in Connected() method
    if (!(current is SmallGrid smallGrid))
      return;

    var neighbors = smallGrid.Connected();
    if (neighbors == null || neighbors.Count == 0)
      return;

    // Create a copy to avoid collection modification during recursion
    var neighborsCopy = new List<SmallGrid>(neighbors);

    // Recursively explore each neighbor of the same network type
    foreach (var neighbor in neighborsCopy)
    {
      if (neighbor == null || visited.Contains(neighbor))
        continue;

      var neighborType = GetNetworkType(neighbor);

      // Only explore if same network type (cables with cables, pipes with pipes, etc.)
      if (neighborType == networkType)
      {
        ExploreRecursive(neighbor, networkType, visited, result);
      }
    }
  }

  private NetworkType GetNetworkType(Structure structure)
  {
    if (structure is Cable)
      return NetworkType.Cable;
    if (structure is Pipe)
      return NetworkType.Pipe;
    if (structure is Chute)
      return NetworkType.Chute;

    return NetworkType.Unknown;
  }
}

/// <summary>
/// Type of network element.
/// </summary>
public enum NetworkType
{
  Unknown,
  Cable,
  Pipe,
  Chute
}
