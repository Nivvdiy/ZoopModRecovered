using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Networks;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Detects and explores connected structures for bulk operations.
/// Uses the game's native Network system (CableNetwork, PipeNetwork, ChuteNetwork).
/// </summary>
public class BulkDetector
{
  /// <summary>
  /// Explores the entire group of connected structures using the game's Network system.
  /// Much faster and more reliable than iterative exploration!
  /// </summary>
  /// <param name="startStructure">The structure to start exploration from</param>
  /// <returns>List of all connected structures in the bulk</returns>
  public List<Structure> ExploreBulk(Structure startStructure)
  {
    if (startStructure == null)
    {
      ZoopLog.Warn("[BulkDetector] ExploreBulk called with NULL startStructure");
      return new List<Structure>();
    }

    List<Structure> result = new List<Structure>();

    // Use the game's native Network system to get all connected structures
    if (startStructure is Cable cable && cable.CableNetwork != null)
    {
      if (cable.CableNetwork.CableList != null)
      {
        result.AddRange(cable.CableNetwork.CableList);
      }
    }
    else if (startStructure is Pipe pipe && pipe.PipeNetwork != null)
    {
      if (pipe.PipeNetwork.StructureList != null)
      {
        foreach (var networkedStructure in pipe.PipeNetwork.StructureList)
        {
          if (networkedStructure is Structure structure)
          {
            result.Add(structure);
          }
        }
      }
    }
    else if (startStructure is Chute chute && chute.ChuteNetwork != null)
    {
      if (chute.ChuteNetwork.StructureList != null)
      {
        foreach (var networkedStructure in chute.ChuteNetwork.StructureList)
        {
          if (networkedStructure is Structure structure)
          {
            result.Add(structure);
          }
        }
      }
    }
    else
    {
      ZoopLog.Warn($"[BulkDetector] Structure {startStructure.PrefabName} has no valid Network or is unsupported type");
      result.Add(startStructure);
    }

    return result;
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
