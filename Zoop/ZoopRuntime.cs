namespace ZoopMod.Zoop;

/// <summary>
/// Composes the single live zoop controller instance used by the Harmony patch layer.
/// </summary>
internal static class ZoopRuntime
{
  private static readonly ZoopSession Session = new();
  private static readonly ZoopPreviewFactory PreviewFactory = new(Session);
  private static readonly ZoopPreviewValidator PreviewValidator =
    new(ZoopConstructableResolver.ResolveBuildIndex, ZoopConstructableResolver.GetConstructableForBuildIndex,
      allowPlacementUpdate => Session.AllowPlacementUpdate = allowPlacementUpdate);

  public static ZoopController Controller { get; } = new(Session, PreviewFactory, PreviewValidator);
}
