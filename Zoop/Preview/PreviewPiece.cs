using Assets.Scripts.Objects;

namespace ZoopMod.Zoop.Preview;

internal sealed class PreviewPiece(Structure structure, int buildIndex)
{
  public Structure Structure { get; } = structure;
  public int BuildIndex { get; } = buildIndex;
}
