using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using Objects.Structures;

namespace ZoopMod.Zoop;

internal static class ZoopConstructableRules
{
  public static bool IsAllowed(Structure constructionCursor)
  {
    return constructionCursor is Pipe or Cable or Chute or Frame or Wall;
  }

  public static bool SupportsWaypoints(Structure constructionCursor)
  {
    return constructionCursor is not Frame && constructionCursor is not Wall;
  }

  public static bool SupportsCornerVariant(List<Structure> constructables, int selectedIndex)
  {
    if (constructables == null || selectedIndex < 0 || selectedIndex >= constructables.Count)
    {
      return false;
    }

    var selectedStructure = constructables[selectedIndex];
    if (selectedStructure == null)
    {
      return false;
    }

    return constructables.Any(structure =>
      IsCornerVariant(structure) &&
      IsMatchingCornerFamily(selectedStructure, structure));
  }

  public static int ResolvePreviewBuildIndex(List<Structure> constructables, int selectedIndex, bool isCorner,
    bool supportsCornerVariant)
  {
    if (constructables == null || selectedIndex < 0 || selectedIndex >= constructables.Count)
    {
      return selectedIndex;
    }

    if (isCorner)
    {
      return 1;
    }

    if (!supportsCornerVariant)
    {
      return selectedIndex;
    }

    var activeItem = constructables[selectedIndex];
    return activeItem switch
    {
      Pipe or Cable or Frame when selectedIndex != 0 => 0,
      Chute when selectedIndex != 0 && selectedIndex != 2 => 0,
      _ => selectedIndex
    };
  }

  private static bool IsCornerVariant(Structure structure)
  {
    if (structure == null)
    {
      return false;
    }

    var prefabName = structure.GetPrefabName();
    if (!string.IsNullOrEmpty(prefabName) &&
        prefabName.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      return true;
    }

    return structure.GetType().Name.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsMatchingCornerFamily(Structure selectedStructure, Structure cornerStructure)
  {
    return selectedStructure switch
    {
      Chute => cornerStructure is Chute,
      Cable => cornerStructure is Cable,
      Frame => cornerStructure is Frame,
      Pipe => cornerStructure is Pipe,
      _ => false
    };
  }
}
