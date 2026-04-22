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
/// Patches ToolUse.Deconstruct to completely replace bulk deconstruction.
/// Uses Prefix to block game's deconstruction and handle it ourselves.
/// </summary>
[HarmonyPatch(typeof(ToolUse), "Deconstruct")]
internal static class ToolUseDeconstructPatch
{
  [UsedImplicitly]
  public static bool Prefix(ToolUse __instance, ConstructionEventInstance eventInstance)
  {
    ZoopLog.Debug($"[BulkDeconstruction] ToolUse.Deconstruct Prefix - IsActive: {BulkDeconstructionRuntime.IsActive}");

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

    // Start our own bulk deconstruction (with proper item spawning)
    BulkDeconstructionRuntime.StartProgressiveDeconstruction();

    // Block the game's default deconstruction
    return false;
  }
}

/// <summary>
/// Hooks Structure.OnDeconstruct event as backup detection.
/// </summary>
internal static class StructureDeconstructionHook
{
  private static bool _isHookRegistered = false;

  public static void Initialize()
  {
    if (_isHookRegistered)
      return;

    Structure.OnDeconstruct += OnStructureDeconstruct;
    _isHookRegistered = true;

    ZoopLog.Info("[BulkDeconstruction] Registered Structure.OnDeconstruct hook");
  }

  private static void OnStructureDeconstruct(Structure structure)
  {
    // Only log if our mode is active (for debugging)
    if (BulkDeconstructionRuntime.IsActive)
    {
      var target = BulkDeconstructionRuntime.CurrentTarget;
      if (target != null && structure == target)
      {
        ZoopLog.Debug($"[BulkDeconstruction] Structure deconstructed: {structure.PrefabName}");
      }
    }
  }
}

/// <summary>
/// Runtime state for bulk deconstruction system.
/// </summary>
public static class BulkDeconstructionRuntime
{
  private static BulkDeconstructionController _controller;

  public static void Initialize(BulkDeconstructionController controller)
  {
    _controller = controller;
    StructureDeconstructionHook.Initialize();
  }

  public static bool IsActive => _controller?.IsActive ?? false;
  public static Structure CurrentTarget => _controller?.CurrentTarget;
  public static BulkValidator.ValidationResult CurrentValidation => _controller?.CurrentValidation;
  public static int CurrentBulkSize => _controller?.CurrentBulkSize ?? 0;

  public static void StartProgressiveDeconstruction()
  {
    _controller?.StartProgressiveDeconstruction();
  }

  public static void Reset()
  {
    // Reset any flags if needed
  }
}
