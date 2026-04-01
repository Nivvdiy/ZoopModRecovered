using System.Collections.Generic;
using Assets.Scripts;
using UnityEngine;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Core;

internal sealed class ZoopDraft
{
  public readonly List<PreviewPiece> PreviewPieces = [];
  public readonly List<Vector3> Waypoints = [];

  public bool HasError { get; set; }
  public ICreativeSpawnable ZoopSpawnPrefab { get; set; }
  public Quaternion ZoopStartRotation { get; set; } = Quaternion.identity;
  public Vector3 ZoopStartWallNormal { get; set; } = Vector3.zero;

  public int PreviewCount => PreviewPieces.Count;
  public int TotalCellCost { get; set; }
  public int TotalResourceCost { get; set; }

  public void ClearPreviewPieces()
  {
    PreviewPieces.Clear();
    TotalCellCost = 0;
    TotalResourceCost = 0;
  }
}
