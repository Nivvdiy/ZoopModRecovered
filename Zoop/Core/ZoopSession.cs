using System.Collections.Generic;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Objects;
using UnityEngine;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Core;

internal sealed class ZoopSession
{
  private int placementUpdateScopeDepth;

  public readonly List<PreviewPiece> PreviewPieces = [];
  public readonly List<Structure> StraightCache = [];
  public readonly List<int> StraightCacheBuildIndices = [];
  public readonly List<Structure> CornerCache = [];
  public readonly List<int> CornerCacheBuildIndices = [];
  public readonly List<Vector3> Waypoints = [];

  public bool HasError { get; set; }
  public CancellationTokenSource CancellationSource { get; set; }
  public ICreativeSpawnable ZoopSpawnPrefab { get; set; }
  public Quaternion ZoopStartRotation { get; set; } = Quaternion.identity;
  public Vector3 ZoopStartWallNormal { get; set; } = Vector3.zero;
  public bool AllowPlacementUpdate => placementUpdateScopeDepth > 0;

  public int PreviewCount => PreviewPieces.Count;

  public System.IDisposable BeginPlacementUpdateScope()
  {
    placementUpdateScopeDepth++;
    return new PlacementUpdateScope(this);
  }

  public void ClearPreviewPieces()
  {
    PreviewPieces.Clear();
  }

  public void ResetActiveZoopState()
  {
    ClearPreviewPieces();
    Waypoints.Clear();
    HasError = false;
    CancellationSource = null;
    ZoopSpawnPrefab = null;
    ZoopStartRotation = Quaternion.identity;
    ZoopStartWallNormal = Vector3.zero;
    placementUpdateScopeDepth = 0;
  }

  private void EndPlacementUpdateScope()
  {
    if (placementUpdateScopeDepth > 0)
    {
      placementUpdateScopeDepth--;
    }
  }

  private sealed class PlacementUpdateScope(ZoopSession session) : System.IDisposable
  {
    private ZoopSession currentSession = session;

    public void Dispose()
    {
      currentSession?.EndPlacementUpdateScope();
      currentSession = null;
    }
  }
}
