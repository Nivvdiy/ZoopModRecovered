using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace ZoopMod.Zoop.Placement;

internal readonly struct LongVariant(int buildIndex, int cellSpan)
{
  public int BuildIndex { get; } = buildIndex;
  public int CellSpan { get; } = cellSpan;
}

internal static class ZoopLongVariantRules
{
  /// <summary>
  /// Scans the constructables list for long variants (cell span > 1) and returns them sorted by
  /// cell span descending so the greedy packing algorithm prefers the longest pieces first.
  /// Long variants are detected by matching the base straight piece's prefab name + trailing digits
  /// (e.g. StructureChuteStraight → StructureChuteStraight3, StructureChuteStraight5, StructureChuteStraight10).
  /// </summary>
  public static List<LongVariant> FindLongVariants(List<Structure> constructables)
  {
    var variants = new List<LongVariant>();
    if (constructables == null || constructables.Count == 0) return variants;

    // The base straight piece is always at index 0.
    var basePiece = constructables[0];
    if (basePiece == null) return variants;

    var baseName = basePiece.GetPrefabName();
    if (string.IsNullOrEmpty(baseName)) return variants;

    for (var i = 1; i < constructables.Count; i++)
    {
      var structure = constructables[i];
      if (structure == null) continue;

      var prefabName = structure.GetPrefabName();
      if (string.IsNullOrEmpty(prefabName)) continue;

      // Must start with the base name and have trailing digits only.
      if (prefabName.Length <= baseName.Length) continue;
      if (!prefabName.StartsWith(baseName, StringComparison.Ordinal)) continue;

      var spanValue = 0;
      var allDigits = true;
      for (var c = baseName.Length; c < prefabName.Length; c++)
      {
        if (char.IsDigit(prefabName[c]))
        {
          spanValue = spanValue * 10 + (prefabName[c] - '0');
        }
        else
        {
          allDigits = false;
          break;
        }
      }

      if (allDigits && spanValue > 1)
      {
        variants.Add(new LongVariant(i, spanValue));
      }
    }

    variants.Sort((a, b) => b.CellSpan.CompareTo(a.CellSpan));
    return variants;
  }

  /// <summary>
  /// Plans how to fill a straight run of <paramref name="cellCount"/> cells using available long variants.
  /// Populates <paramref name="result"/> with cell spans (1 for single pieces, >1 for long variants).
  /// Uses a greedy algorithm, placing the longest fitting variant first.
  /// </summary>
  public static void PlanRun(
    int cellCount,
    List<LongVariant> longVariants,
    List<int> result)
  {
    result.Clear();
    if (cellCount <= 0) return;

    var remaining = cellCount;
    while (remaining > 0)
    {
      var placed = false;
      for (var v = 0; v < longVariants.Count; v++)
      {
        if (longVariants[v].CellSpan <= remaining)
        {
          result.Add(longVariants[v].CellSpan);
          remaining -= longVariants[v].CellSpan;
          placed = true;
          break;
        }
      }

      if (!placed)
      {
        result.Add(1);
        remaining--;
      }
    }
  }

  /// <summary>
  /// Plans a run by splitting it into sections around separator cells.
  /// Separators (start point, end point, corners, waypoints, barrier/merge cells) are always span-1.
  /// Contiguous non-separator cells form sections that are independently packed with long variants.
  /// </summary>
  public static void PlanSections(
    int cellCount,
    List<LongVariant> longVariants,
    HashSet<int> separators,
    List<int> result)
  {
    result.Clear();
    if (cellCount <= 0) return;

    if (separators == null || separators.Count == 0)
    {
      PlanRun(cellCount, longVariants, result);
      return;
    }

    var subResult = new List<int>();
    var sectionStart = 0;

    for (var i = 0; i <= cellCount; i++)
    {
      var isSeparator = i < cellCount && separators.Contains(i);

      if (isSeparator || i == cellCount)
      {
        var sectionLength = i - sectionStart;
        if (sectionLength > 0)
        {
          PlanRun(sectionLength, longVariants, subResult);
          result.AddRange(subResult);
        }

        if (isSeparator)
        {
          result.Add(1);
          sectionStart = i + 1;
        }
      }
    }
  }

  /// <summary>
  /// Returns the build index of the long variant with the given cell span, or -1 if not found.
  /// </summary>
  public static int GetBuildIndexForSpan(List<LongVariant> longVariants, int cellSpan)
  {
    for (var i = 0; i < longVariants.Count; i++)
    {
      if (longVariants[i].CellSpan == cellSpan) return longVariants[i].BuildIndex;
    }

    return -1;
  }

  /// <summary>
  /// Extracts the cell span from a structure's prefab name by comparing against the base straight piece name.
  /// Returns 1 for normal (non-long) pieces.
  /// </summary>
  public static int DetectCellSpan(Structure structure, string basePrefabName)
  {
    if (structure == null || string.IsNullOrEmpty(basePrefabName)) return 1;
    var prefabName = structure.GetPrefabName();
    if (string.IsNullOrEmpty(prefabName)) return 1;
    if (prefabName.Length <= basePrefabName.Length) return 1;
    if (!prefabName.StartsWith(basePrefabName, StringComparison.Ordinal)) return 1;

    var spanValue = 0;
    for (var i = basePrefabName.Length; i < prefabName.Length; i++)
    {
      if (char.IsDigit(prefabName[i]))
      {
        spanValue = spanValue * 10 + (prefabName[i] - '0');
      }
      else
      {
        return 1;
      }
    }

    return spanValue > 1 ? spanValue : 1;
  }
}
