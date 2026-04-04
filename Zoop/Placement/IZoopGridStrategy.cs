using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Placement;

/// <summary>
/// Encapsulates the grid-type-specific preview update logic for a single cursor family
/// (e.g. SmallGrid, LargeStructure). Implement this interface on each coordinator and
/// register it in <see cref="ZoopPreviewCoordinator"/> to add support for a new grid type.
/// </summary>
internal interface IZoopGridStrategy
{
  /// <summary>Returns true when this strategy handles the given construction cursor.</summary>
  bool Matches(Structure cursor);

  /// <summary>Whether this grid type supports multi-waypoint zoop paths.</summary>
  bool SupportsWaypoints { get; }

  /// <summary>Returns the world-space snapped position for the given cursor, or null if unavailable.</summary>
  Vector3? GetCursorPosition(Structure cursor);

  UniTask UpdatePreview(ZoopDraft draft, ZoopPreviewCache previewCache,
    InventoryManager inventoryManager, Vector3 currentPos);
}
