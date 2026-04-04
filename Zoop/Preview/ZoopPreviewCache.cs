using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace ZoopMod.Zoop.Preview;

/// <summary>A pooled preview structure paired with the build-index it was instantiated for.</summary>
internal readonly struct CachedStructure(Structure instance, int buildIndex)
{
  public Structure Instance { get; } = instance;
  public int BuildIndex { get; } = buildIndex;
}

internal sealed class ZoopPreviewCache
{
  public readonly List<CachedStructure> StraightCache = [];
  public readonly List<CachedStructure> CornerCache = [];
  public readonly Dictionary<int, List<CachedStructure>> LongCaches = new();
}
