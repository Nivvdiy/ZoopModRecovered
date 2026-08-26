using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
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

    // Stationeers has changed these network collections between fields and properties.
    // Resolve them by name so the mod does not encode the underlying member shape in IL.
    if (startStructure is Cable cable && cable.CableNetwork != null)
    {
      AddNetworkStructures(cable.CableNetwork, "CableList", result);
    }
    else if (startStructure is Pipe pipe && pipe.PipeNetwork != null)
    {
      AddNetworkStructures(pipe.PipeNetwork, "StructureList", result);
    }
    else if (startStructure is Chute chute && chute.ChuteNetwork != null)
    {
      AddNetworkStructures(chute.ChuteNetwork, "StructureList", result);
    }
    else
    {
      ZoopLog.Warn($"[BulkDetector] Structure {startStructure.PrefabName} has no valid Network or is unsupported type");
      result.Add(startStructure);
    }

    return result;
  }

  private static void AddNetworkStructures(object network, string memberName, List<Structure> result)
  {
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var networkType = network.GetType();
    object memberValue = null;

    var property = networkType.GetProperty(memberName, flags);
    if (property != null)
    {
      memberValue = property.GetValue(network, null);
    }
    else
    {
      var field = networkType.GetField(memberName, flags);
      if (field != null)
      {
        memberValue = field.GetValue(network);
      }
    }

    if (!(memberValue is IEnumerable networkStructures))
    {
      ZoopLog.Warn($"[BulkDetector] {networkType.Name}.{memberName} is unavailable");
      return;
    }

    foreach (var networkedStructure in networkStructures)
    {
      if (networkedStructure is Structure structure)
      {
        result.Add(structure);
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
