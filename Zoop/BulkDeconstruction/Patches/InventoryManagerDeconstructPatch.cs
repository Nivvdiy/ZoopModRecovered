using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;
using JetBrains.Annotations;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.BulkDeconstruction.Patches;

/// <summary>
/// NOTE: WaitUntilDone is NOT used for deconstruction in Stationeers.
/// The game calls ToolUse.Deconstruct directly without a wait timer.
/// We only patch ToolUse.Deconstruct to intercept the action.
/// </summary>

/// <summary>
/// Patches ToolUse.Deconstruct to extend vanilla deconstruction.
/// The clicked structure must deconstruct normally so ToolUse.SpawnItem returns its item.
/// </summary>
[HarmonyPatch(typeof(ToolUse), "Deconstruct")]
internal static class ToolUseDeconstructPatch
{
  [UsedImplicitly]
  public static bool Prefix(ToolUse __instance, ConstructionEventInstance eventInstance)
  {
    ZoopLog.Debug($"[BulkDeconstruction] ToolUse.Deconstruct Prefix - IsActive: {BulkDeconstructionRuntime.IsActive}");

    // Bulk execution deliberately re-enters vanilla deconstruction through AttackWith.
    // Let those calls pass so ToolUse.SpawnItem and the server-authoritative destroy path still run.
    if (BulkDeconstructionRuntime.AllowVanillaDeconstruct)
    {
      ZoopLog.Debug("[BulkDeconstruction] Allowing vanilla deconstruction requested by bulk executor");
      return true;
    }

    // Only intercept if bulk deconstruction mode is active
    if (!BulkDeconstructionRuntime.IsActive)
    {
      ZoopLog.Debug("[BulkDeconstruction] Mode not active, allowing normal deconstruction");
      return true; // Allow normal deconstruction
    }

    // Check if we should execute bulk deconstruction
    var target = BulkDeconstructionRuntime.CurrentTarget;
    if (target == null)
    {
      ZoopLog.Debug("[BulkDeconstruction] No target, allowing normal deconstruction");
      return true;
    }

    // Check validation
    var validation = BulkDeconstructionRuntime.CurrentValidation;
    if (validation == null || !validation.CanDeconstruct)
    {
      ZoopLog.Info($"[BulkDeconstruction] Blocked: {validation?.Reason ?? "Invalid"}");
      return false; // Block deconstruction
    }

    ZoopLog.Info($"[BulkDeconstruction] Intercepting deconstruction, will handle {BulkDeconstructionRuntime.CurrentBulkSize} structures");

    // Let vanilla deconstruct the clicked structure first. This method is the vanilla item
    // recovery point; blocking it destroys the structure but skips the returned item.
    BulkDeconstructionRuntime.StartBulkAfterVanillaDeconstruct();

    return true;
  }

  [UsedImplicitly]
  public static void Postfix()
  {
    if (BulkDeconstructionRuntime.ConsumeStartBulkAfterVanillaDeconstruct())
    {
      BulkDeconstructionRuntime.StartProgressiveDeconstruction();
    }
  }
}

/// <summary>
/// Runtime state for bulk deconstruction system.
/// </summary>
public static class BulkDeconstructionRuntime
{
  private static BulkDeconstructionController _controller;
  private static int _allowVanillaDeconstructDepth;
  private static bool _startBulkAfterVanillaDeconstruct;

  public static void Initialize(BulkDeconstructionController controller)
  {
    _controller = controller;
  }

  public static bool IsActive => _controller?.IsActive ?? false;
  public static bool AllowVanillaDeconstruct => _allowVanillaDeconstructDepth > 0;
  public static Structure CurrentTarget => _controller?.CurrentTarget;
  public static BulkValidator.ValidationResult CurrentValidation => _controller?.CurrentValidation;
  public static int CurrentBulkSize => _controller?.CurrentBulkSize ?? 0;

  public static void StartBulkAfterVanillaDeconstruct()
  {
    _startBulkAfterVanillaDeconstruct = true;
  }

  public static bool ConsumeStartBulkAfterVanillaDeconstruct()
  {
    if (!_startBulkAfterVanillaDeconstruct || AllowVanillaDeconstruct)
    {
      return false;
    }

    _startBulkAfterVanillaDeconstruct = false;
    return true;
  }

  public static void StartProgressiveDeconstruction()
  {
    _controller?.StartProgressiveDeconstruction();
  }

  public static void Reset()
  {
    _allowVanillaDeconstructDepth = 0;
    _startBulkAfterVanillaDeconstruct = false;
  }

  public static void RunVanillaDeconstruct(Action action)
  {
    // Depth instead of bool keeps this safe if vanilla deconstruction synchronously nests calls.
    _allowVanillaDeconstructDepth++;
    try
    {
      action();
    }
    finally
    {
      _allowVanillaDeconstructDepth--;
    }
  }
}
