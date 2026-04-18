using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;
using JetBrains.Annotations;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.NetworkDeconstruction.Patches;

/// <summary>
/// NOTE: WaitUntilDone is NOT used for deconstruction in Stationeers.
/// The game calls ToolUse.Deconstruct directly without a wait timer.
/// We only patch ToolUse.Deconstruct to intercept the action.
/// </summary>

/// <summary>
/// Patches ToolUse.Deconstruct to completely replace network deconstruction.
/// Uses Prefix to block game's deconstruction and handle it ourselves.
/// </summary>
[HarmonyPatch(typeof(ToolUse), "Deconstruct")]
internal static class ToolUseDeconstructPatch
{
  [UsedImplicitly]
  public static bool Prefix(ToolUse __instance, ConstructionEventInstance eventInstance)
  {
    ZoopLog.Debug($"[NetworkDeconstruction] ToolUse.Deconstruct Prefix - IsActive: {NetworkDeconstructionRuntime.IsActive}");

    // Only intercept if network deconstruction mode is active
    if (!NetworkDeconstructionRuntime.IsActive)
    {
      ZoopLog.Debug("[NetworkDeconstruction] Mode not active, allowing normal deconstruction");
      return true; // Allow normal deconstruction
    }

    // Check if we should execute network deconstruction
    var target = NetworkDeconstructionRuntime.CurrentTarget;
    if (target == null)
    {
      ZoopLog.Debug("[NetworkDeconstruction] No target, allowing normal deconstruction");
      return true;
    }

    // Check validation
    var validation = NetworkDeconstructionRuntime.CurrentValidation;
    if (validation == null || !validation.CanDeconstruct)
    {
      ZoopLog.Info($"[NetworkDeconstruction] Blocked: {validation?.Reason ?? "Invalid"}");
      return false; // Block deconstruction
    }

    ZoopLog.Info($"[NetworkDeconstruction] Intercepting deconstruction, will handle {NetworkDeconstructionRuntime.CurrentNetworkSize} structures");

    // Start our own network deconstruction (with proper item spawning)
    NetworkDeconstructionRuntime.StartProgressiveDeconstruction();

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

    ZoopLog.Info("[NetworkDeconstruction] Registered Structure.OnDeconstruct hook");
  }

  private static void OnStructureDeconstruct(Structure structure)
  {
    // Only log if our mode is active (for debugging)
    if (NetworkDeconstructionRuntime.IsActive)
    {
      var target = NetworkDeconstructionRuntime.CurrentTarget;
      if (target != null && structure == target)
      {
        ZoopLog.Debug($"[NetworkDeconstruction] Structure deconstructed: {structure.PrefabName}");
      }
    }
  }
}

/// <summary>
/// Runtime state for network deconstruction system.
/// </summary>
public static class NetworkDeconstructionRuntime
{
  private static NetworkDeconstructionController _controller;

  public static void Initialize(NetworkDeconstructionController controller)
  {
    _controller = controller;
    StructureDeconstructionHook.Initialize();
  }

  public static bool IsActive => _controller?.IsActive ?? false;
  public static Structure CurrentTarget => _controller?.CurrentTarget;
  public static NetworkValidator.ValidationResult CurrentValidation => _controller?.CurrentValidation;
  public static int CurrentNetworkSize => _controller?.CurrentNetworkSize ?? 0;

  public static void StartProgressiveDeconstruction()
  {
    _controller?.StartProgressiveDeconstruction();
  }

  public static void Reset()
  {
    // Reset any flags if needed
  }
}
