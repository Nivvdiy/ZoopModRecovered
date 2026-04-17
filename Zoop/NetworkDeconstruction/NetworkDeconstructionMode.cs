using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
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
/// Main controller for network deconstruction mode.
/// Handles activation, tool tracking, network detection, and UI display.
/// 
/// Safety: Only runs when a save is loaded and InventoryManager is initialized.
/// Automatically deactivates when save is unloaded or inventory becomes unavailable.
/// </summary>
public class NetworkDeconstructionMode : MonoBehaviour
{
  private static NetworkDeconstructionMode _instance;
  public static NetworkDeconstructionMode Instance => _instance;

  private readonly NetworkDetector _detector = new NetworkDetector();
  private readonly NetworkValidator _validator = new NetworkValidator();
  private readonly NetworkDeconstructionUI _ui = new NetworkDeconstructionUI();

  private bool _isActive;
  private Thing _lastHeldTool;
  private Structure _currentTarget;
  private List<Structure> _currentNetwork;
  private NetworkValidator.ValidationResult _currentValidation;

  public static void Initialize()
  {
    if (_instance != null)
    {
      ZoopLog.Info("[NetworkDeconstruction] Already initialized");
      return;
    }

    var gameObject = new GameObject("NetworkDeconstructionMode");
    DontDestroyOnLoad(gameObject);
    _instance = gameObject.AddComponent<NetworkDeconstructionMode>();
    ZoopLog.Info("[NetworkDeconstruction] Initialized successfully");
  }

  private void Update()
  {
    // Only run when a save is loaded and inventory system is available
    if (InventoryManager.Instance == null || InventoryManager.Instance.ActiveHand == null)
    {
      // If mode was active but inventory is gone (save unloaded), deactivate
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

    // Update detection if active
    if (_isActive)
    {
      UpdateDetection();
    }
  }

  private void OnGUI()
  {
    if (!_isActive || _currentTarget == null || _currentNetwork == null)
      return;

    _ui.Render(_currentTarget, _currentNetwork, _currentValidation);
  }

  private void TryActivateMode()
  {
    if (!HasValidTool())
    {
      ZoopLog.Info("[NetworkDeconstruction] Cannot activate: No valid tool equipped (need Wire Cutters or Wrench)");
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
    ZoopLog.Info($"[NetworkDeconstruction] Mode deactivated: {reason}");
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
    // Cable requires wire cutters
    if (structure is Cable && IsHoldingWireCutters())
      return true;

    // Pipe and chute require wrench
    if ((structure is Pipe || structure is Chute) && IsHoldingWrench())
      return true;

    return false;
  }

  private Thing GetCurrentTool()
  {
    // Safe check: return null if inventory system is not ready
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
