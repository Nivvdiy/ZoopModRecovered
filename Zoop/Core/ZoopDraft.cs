using System.Collections.Generic;
using Assets.Scripts;
using UnityEngine;
using ZoopMod.Zoop.Preview;

namespace ZoopMod.Zoop.Core;

/// <summary>
/// Immutable configuration captured once at <c>BeginZoop</c>. Held by <see cref="ZoopDraft"/>
/// so all consumers receive it through the existing draft reference with no signature changes.
/// </summary>
internal readonly struct ZoopSessionConfig(
  ICreativeSpawnable spawnPrefab,
  Quaternion startRotation,
  Vector3 startWallNormal)
{
  /// <summary>The prefab to spawn when placing in authoring mode. May be null for normal placement.</summary>
  public ICreativeSpawnable SpawnPrefab { get; } = spawnPrefab;

  /// <summary>World rotation of the construction cursor at the moment the zoop started.</summary>
  public Quaternion StartRotation { get; } = startRotation;

  /// <summary>
  /// Snapped cardinal wall-normal captured at zoop start. <see cref="Vector3.zero"/> for non-wall items.
  /// </summary>
  public Vector3 StartWallNormal { get; } = startWallNormal;
}

/// <summary>
/// Mutable state for one active zoop session.
/// <para>
/// Stable session identity is in <see cref="Session"/>; it is written once at <c>BeginZoop</c>
/// and never changed. Everything else is reset and rebuilt on every preview update cycle.
/// </para>
/// </summary>
internal sealed class ZoopDraft
{
  public readonly List<PreviewPiece> PreviewPieces = [];
  public readonly List<Vector3> Waypoints = [];

  /// <summary>Immutable configuration captured at zoop start.</summary>
  public ZoopSessionConfig Session { get; set; }

  public bool HasError { get; set; }

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
