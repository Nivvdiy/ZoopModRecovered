using System;
using System.Collections.Generic;
using System.Linq;
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

      if (TryParseTrailingSpan(prefabName, baseName, out var spanValue))
        variants.Add(new LongVariant(i, spanValue));
    }

    variants.Sort((a, b) => b.CellSpan.CompareTo(a.CellSpan));
    return variants;
  }

  // Returns true when prefabName is baseName followed by an integer > 1 (e.g. "BaseStraight5").
  private static bool TryParseTrailingSpan(string prefabName, string baseName, out int spanValue)
  {
    spanValue = 0;
    if (prefabName.Length <= baseName.Length) return false;
    if (!prefabName.StartsWith(baseName, StringComparison.Ordinal)) return false;

    for (var c = baseName.Length; c < prefabName.Length; c++)
    {
      if (!char.IsDigit(prefabName[c])) return false;
      spanValue = spanValue * 10 + (prefabName[c] - '0');
    }

    return spanValue > 1;
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
      foreach (var t in longVariants)
      {
        if (t.CellSpan > remaining)
        {
          continue;
        }

        result.Add(t.CellSpan);
        remaining -= t.CellSpan;
        placed = true;
        break;
      }

      if (placed)
      {
        continue;
      }

      result.Add(1);
      remaining--;
    }
  }

  /// <summary>
  /// Returns the build index of the long variant with the given cell span, or -1 if not found.
  /// </summary>
  public static int GetBuildIndexForSpan(List<LongVariant> longVariants, int cellSpan)
  {
    return longVariants
      .Where(t => t.CellSpan == cellSpan)
      .Select(t => t.BuildIndex)
      .DefaultIfEmpty(-1)
      .First();
  }
}
