using System.Collections.Generic;
using Assets.Scripts;
using UnityEngine;

namespace ZoopMod.Zoop.Core;

internal sealed class ZoopBuildPlan(IReadOnlyList<ZoopBuildPiece> pieces)
{
  public IReadOnlyList<ZoopBuildPiece> Pieces { get; } = pieces;
  public int Count => Pieces.Count;
}

internal sealed class ZoopBuildPiece(ICreativeSpawnable spawnPrefab, int buildIndex, Vector3 position, Quaternion rotation)
{
  public ICreativeSpawnable SpawnPrefab { get; } = spawnPrefab;
  public int BuildIndex { get; } = buildIndex;
  public Vector3 Position { get; } = position;
  public Quaternion Rotation { get; } = rotation;
}
