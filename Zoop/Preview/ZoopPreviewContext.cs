using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Preview;

/// <summary>
/// Holds the arguments that are invariant for every piece in a single structure-list build pass,
/// allowing <see cref="ZoopPreviewFactory.AddStructure"/> to stay within a reasonable parameter count.
/// </summary>
internal sealed class ZoopPreviewContext(
  ZoopDraft draft,
  ZoopPreviewCache previewCache,
  List<Structure> constructables,
  InventoryManager inventoryManager,
  bool supportsCornerVariant)
{
  public ZoopDraft Draft { get; } = draft;
  public ZoopPreviewCache PreviewCache { get; } = previewCache;
  public List<Structure> Constructables { get; } = constructables;
  public InventoryManager InventoryManager { get; } = inventoryManager;
  public bool SupportsCornerVariant { get; } = supportsCornerVariant;
}
