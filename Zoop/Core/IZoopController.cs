using Assets.Scripts.Inventory;

namespace ZoopMod.Zoop.Core;

internal interface IZoopController
{
  bool IsPreviewing { get; }
  ZoopDraft ActiveDraft { get; }
  ZoopPreviewCache ActivePreviewCache { get; }
  void CancelZoop();
  void EnterPendingBuildState();
  void ResetSessionForBuild();
  void ResumePreviewing(InventoryManager inventoryManager);
}
