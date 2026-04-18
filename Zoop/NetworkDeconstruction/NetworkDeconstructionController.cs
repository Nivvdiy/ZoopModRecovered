using System.Collections;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using UnityEngine;
using ZoopMod.Zoop.EntryPoints.Input;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Controller for network deconstruction system.
/// Handles detection, validation, and progressive deconstruction of network elements.
/// Similar to ZoopController but for deconstruction instead of construction.
/// </summary>
public class NetworkDeconstructionController : MonoBehaviour
{
  private static NetworkDeconstructionController _instance;
  public static NetworkDeconstructionController Instance => _instance;

  private readonly NetworkDetector _detector = new NetworkDetector();
  private readonly NetworkValidator _validator = new NetworkValidator();
  private readonly NetworkDeconstructionUI _ui = new NetworkDeconstructionUI();
  private readonly NetworkItemRecovery _itemRecovery = new NetworkItemRecovery();

  private bool _isActive;
  private Thing _lastHeldTool;
  private Structure _currentTarget;
  private List<Structure> _currentNetwork;
  private NetworkValidator.ValidationResult _currentValidation;
  private bool _isDeconstructing; // Currently executing deconstruction
  private Coroutine _deconstructionCoroutine;

  // Public properties for patches
  public bool IsActive => _isActive;
  public Structure CurrentTarget => _currentTarget;
  public NetworkValidator.ValidationResult CurrentValidation => _currentValidation;
  public bool IsDeconstructing => _isDeconstructing;
  public int CurrentNetworkSize => _currentNetwork?.Count ?? 0;

  public static void Initialize()
  {
    if (_instance != null)
    {
      ZoopLog.Info("[NetworkDeconstruction] Already initialized");
      return;
    }

    var gameObject = new GameObject("NetworkDeconstructionController");
    DontDestroyOnLoad(gameObject);
    _instance = gameObject.AddComponent<NetworkDeconstructionController>();

    // Register with runtime
    Patches.NetworkDeconstructionRuntime.Initialize(_instance);

    ZoopLog.Info("[NetworkDeconstruction] Initialized successfully");
  }

  private void Update()
  {
    // Only run when a save is loaded and inventory system is available
    if (InventoryManager.Instance == null || InventoryManager.Instance.ActiveHand == null)
    {
      if (_isActive)
      {
        DeactivateMode("Save unloaded or inventory not ready");
      }
      return;
    }

    // Detect tool changes and auto-disable mode
    var currentTool = GetCurrentTool();
    if (_lastHeldTool != currentTool)
    {
      _lastHeldTool = currentTool;
      if (_isActive)
      {
        DeactivateMode("Tool changed");
      }
    }

    // Handle key press to toggle mode
    if (UnityEngine.Input.GetKeyDown(ZoopKeyBindings.NetworkDeconstruct))
    {
      if (!_isActive)
      {
        TryActivateMode();
      }
      else
      {
        DeactivateMode("User toggled off");
      }
    }

    // Update detection if active and not currently deconstructing
    if (_isActive && !_isDeconstructing)
    {
      UpdateDetection();
    }
  }

  private void OnGUI()
  {
    if (!_isActive || _currentTarget == null || _currentNetwork == null)
      return;

    _ui.Render(_currentTarget, _currentNetwork, _currentValidation, _isDeconstructing);
  }

  private void TryActivateMode()
  {
    if (!HasValidTool())
    {
      ZoopLog.Info("[NetworkDeconstruction] Cannot activate: No valid tool equipped");
      return;
    }

    _isActive = true;
    ZoopLog.Info("[NetworkDeconstruction] Mode activated");
  }

  private void DeactivateMode(string reason)
  {
    _isActive = false;
    _currentTarget = null;
    _currentNetwork = null;
    _currentValidation = null;

    // Reset patch state
    Patches.NetworkDeconstructionRuntime.Reset();

    // Clear deconstructing flag
    _isDeconstructing = false;

    ZoopLog.Info($"[NetworkDeconstruction] Mode deactivated: {reason}");
  }

  /// <summary>
  /// Called by patch AFTER the first structure was deconstructed normally (items spawned by game).
  /// Starts progressive deconstruction of remaining structures.
  /// TODO: Find a way to spawn items properly for each structure.
  /// </summary>
  public void StartProgressiveDeconstruction()
  {
    if (_currentNetwork == null || _currentNetwork.Count == 0)
    {
      ZoopLog.Info("[NetworkDeconstruction] Cannot start: No network");
      return;
    }

    if (_isDeconstructing)
    {
      ZoopLog.Info("[NetworkDeconstruction] Already deconstructing");
      return;
    }

    ZoopLog.Info($"[NetworkDeconstruction] Starting progressive deconstruction of {_currentNetwork.Count} structures");
    _deconstructionCoroutine = StartCoroutine(DeconstructProgressively());
  }

  /// <summary>
  /// Progressively destroys structures and spawns items.
  /// Logs Thing properties to understand the structure-to-item relationship.
  /// </summary>
  private IEnumerator DeconstructProgressively()
  {
    _isDeconstructing = true;

    int totalStructures = _currentNetwork.Count;
    int removed = 0;

    ZoopLog.Info($"[NetworkDeconstruction] Processing {totalStructures} structures - logging Thing properties");

    foreach (var structure in _currentNetwork)
    {
      if (structure == null || structure.gameObject == null)
        continue;

      // LOG ALL Thing properties to understand what's available
      ZoopLog.Info($"[NetworkDeconstruction] === Structure Debug Info ===");
      ZoopLog.Info($"  PrefabName: {structure.PrefabName}");
      ZoopLog.Info($"  PrefabHash: {structure.PrefabHash}");
      ZoopLog.Info($"  CustomName: {structure.CustomName ?? "null"}");
      ZoopLog.Info($"  DisplayName: {structure.DisplayName}");
      ZoopLog.Info($"  SpawnableName: {structure.SpawnableName}");
      ZoopLog.Info($"  SpawnId: {structure.SpawnId}");
      ZoopLog.Info($"  TotalThings: {Thing.TotalThings}");
      ZoopLog.Info($"  TotalThingsToSpawn: {Thing.TotalThingsToSpawn}");

      // SourcePrefab
      var sourcePrefab = structure.SourcePrefab;
      if (sourcePrefab != null)
      {
        ZoopLog.Info($"  SourcePrefab.PrefabName: {sourcePrefab.PrefabName}");
        ZoopLog.Info($"  SourcePrefab.PrefabHash: {sourcePrefab.PrefabHash}");
      }
      else
      {
        ZoopLog.Info($"  SourcePrefab: null");
      }

      // Type-specific info
      if (structure is Cable cable)
      {
        ZoopLog.Info($"  Cable.CableType: {cable.CableType}");
      }
      else if (structure is Pipe pipe)
      {
        ZoopLog.Info($"  Pipe.PipeType: {pipe.PipeType}");
      }
      else if (structure is Chute chute)
      {
        ZoopLog.Info($"  Chute detected");
      }

      ZoopLog.Info($"[NetworkDeconstruction] ===========================");

      // For now, just destroy without spawning items
      try
      {
        Object.Destroy(structure.gameObject);
        removed++;
      }
      catch (System.Exception ex)
      {
        ZoopLog.Error($"[NetworkDeconstruction] Error removing {structure.PrefabName}: {ex.Message}");
      }

      // Small delay
      if (removed % 3 == 0)
      {
        yield return null;
      }
    }

    ZoopLog.Info($"[NetworkDeconstruction] Complete: {removed}/{totalStructures} structures removed (DEBUG MODE - no items spawned)");

    _isDeconstructing = false;
    _deconstructionCoroutine = null;

    // Deactivate mode after successful deconstruction
    DeactivateMode("Deconstruction complete");
  }

  private void UpdateDetection()
  {
    var camera = Camera.main;
    if (camera == null)
      return;

    var ray = camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));

    if (Physics.Raycast(ray, out RaycastHit hit, NetworkDeconstructionConfig.RaycastDistance))
    {
      var structure = hit.collider.GetComponentInParent<Structure>();

      if (structure != null && IsValidNetworkElement(structure))
      {
        if (_currentTarget != structure)
        {
          _currentTarget = structure;
          _currentNetwork = _detector.ExploreNetwork(structure);
          _currentValidation = _validator.Validate(structure);

          ZoopLog.Debug($"[NetworkDeconstruction] Detected {GetNetworkTypeName(structure)} network: {_currentNetwork.Count} structures");
        }
      }
      else
      {
        ClearCurrentTarget();
      }
    }
    else
    {
      ClearCurrentTarget();
    }
  }

  private void ClearCurrentTarget()
  {
    _currentTarget = null;
    _currentNetwork = null;
    _currentValidation = null;
  }

  private bool IsValidNetworkElement(Structure structure)
  {
    if (structure is Cable && IsHoldingWireCutters())
      return true;

    if ((structure is Pipe || structure is Chute) && IsHoldingWrench())
      return true;

    return false;
  }

  private Thing GetCurrentTool()
  {
    if (InventoryManager.Instance == null)
      return null;

    if (InventoryManager.Instance.ActiveHand == null)
      return null;

    return InventoryManager.Instance.ActiveHand.Slot?.Get();
  }

  private bool HasValidTool()
  {
    return IsHoldingWireCutters() || IsHoldingWrench();
  }

  private bool IsHoldingWireCutters()
  {
    var heldItem = GetCurrentTool();
    return heldItem is WireCutter;
  }

  private bool IsHoldingWrench()
  {
    var heldItem = GetCurrentTool();
    return heldItem is Wrench;
  }

  private string GetNetworkTypeName(Structure structure)
  {
    if (structure is Cable)
      return "Cable";
    if (structure is Pipe)
      return "Pipe";
    if (structure is Chute)
      return "Chute";

    return "Unknown";
  }
}
