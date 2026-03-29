using System.Collections.Generic;

namespace ZoopMod.Zoop.Planning;

internal sealed class SmallGridZoopPlan(List<ZoopSegment> segments, bool isSinglePlacement)
{

  public List<ZoopSegment> Segments { get; } = segments;
  public bool IsSinglePlacement { get; } = isSinglePlacement;
}
