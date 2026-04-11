using Assets.Scripts.Inventory;
using ZoopMod.Zoop.Preview;

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
