using System.Collections.Generic;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;

namespace ZoopMod.Zoop;

internal sealed class ZoopSession
{
  public readonly List<PreviewPiece> PreviewPieces = [];
  public readonly List<Structure> StraightCache = [];
  public readonly List<int> StraightCacheBuildIndices = [];
  public readonly List<Structure> CornerCache = [];
  public readonly List<int> CornerCacheBuildIndices = [];
  public readonly List<Vector3?> Waypoints = [];

  public bool HasError { get; set; }
  public Coroutine ActionCoroutine { get; set; }
  public bool AllowPlacementUpdate { get; set; }
  public CancellationTokenSource CancellationSource { get; set; }
  public InventoryManager ActionCoroutineOwner { get; set; }
  public ICreativeSpawnable ZoopSpawnPrefab { get; set; }
  public Quaternion ZoopStartRotation { get; set; } = Quaternion.identity;
  public Vector3 ZoopStartWallNormal { get; set; } = Vector3.zero;
}
