using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace ZoopMod.Zoop.Core;

internal sealed class ZoopPreviewCache
{
  public readonly List<Structure> StraightCache = [];
  public readonly List<int> StraightCacheBuildIndices = [];
  public readonly List<Structure> CornerCache = [];
  public readonly List<int> CornerCacheBuildIndices = [];
  public readonly Dictionary<int, List<Structure>> LongCaches = new();
  public readonly Dictionary<int, List<int>> LongCacheBuildIndices = new();
}
