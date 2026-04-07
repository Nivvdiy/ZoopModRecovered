namespace ZoopMod.Zoop.Core;

internal sealed class ZoopPlacementUpdateGate
{
  private int scopeDepth;

  public bool AllowPlacementUpdate => scopeDepth > 0;

  public System.IDisposable BeginScope()
  {
    scopeDepth++;
    return new PlacementUpdateScope(this);
  }

  private void EndScope()
  {
    if (scopeDepth > 0)
    {
      scopeDepth--;
    }
  }

  private sealed class PlacementUpdateScope(ZoopPlacementUpdateGate gate) : System.IDisposable
  {
    private ZoopPlacementUpdateGate currentGate = gate;

    public void Dispose()
    {
      currentGate?.EndScope();
      currentGate = null;
    }
  }
}
