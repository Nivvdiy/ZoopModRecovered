using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace ZoopMod.Zoop.Core;

internal sealed class ZoopPreviewCache
{
  public readonly List<Structure> StraightCache = [];
  public readonly List<int> StraightCacheBuildIndices = [];
  public readonly List<Structure> CornerCache = [];
  public readonly List<int> CornerCacheBuildIndices = [];
}
