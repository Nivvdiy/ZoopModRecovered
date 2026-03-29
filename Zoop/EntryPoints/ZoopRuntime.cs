using ZoopMod.Zoop.Core;
using ZoopMod.Zoop.Placement;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.EntryPoints;

/// <summary>
/// Composes the single live zoop controller instance used by the Harmony patch layer.
/// </summary>
internal static class ZoopRuntime
{
  private static readonly ZoopSession Session = new();
  private static readonly ZoopConstructableResolver ConstructableResolver = new(Session);
  private static readonly ZoopPreviewFactory PreviewFactory = new(Session);
  private static readonly ZoopPreviewValidator PreviewValidator = new(Session, ConstructableResolver);

  public static ZoopController Controller { get; } = new(Session, PreviewFactory, PreviewValidator, ConstructableResolver);
}
