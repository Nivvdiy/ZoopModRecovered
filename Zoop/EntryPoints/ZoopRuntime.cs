using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.EntryPoints;

/// <summary>
/// Composes the single live zoop controller instance used by the Harmony patch layer.
/// </summary>
internal static class ZoopRuntime
{
  private static readonly ZoopPlacementUpdateGate PlacementUpdateGate = new();
  private static readonly ZoopPreviewValidator PreviewValidator = new(PlacementUpdateGate);

  public static ZoopController Controller { get; } = new(PreviewValidator, PlacementUpdateGate);
}
