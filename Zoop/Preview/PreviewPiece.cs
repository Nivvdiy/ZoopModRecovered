using Assets.Scripts.Objects;

namespace ZoopMod.Zoop.Preview;

internal sealed class PreviewPiece(Structure structure, int buildIndex, int cellSpan = 1)
{
  public Structure Structure { get; } = structure;
  public int BuildIndex { get; } = buildIndex;
  public int CellSpan { get; } = cellSpan;
}
