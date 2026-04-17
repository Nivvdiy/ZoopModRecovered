using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Validates if a network can be safely deconstructed.
/// Checks conditions like pressure, power, and item presence.
/// </summary>
public class NetworkValidator
{
  /// <summary>
  /// Result of a network validation check.
  /// </summary>
  public class ValidationResult
  {
    public bool CanDeconstruct { get; set; }
    public string Reason { get; set; }

    public static ValidationResult Success() => new ValidationResult { CanDeconstruct = true, Reason = "" };
    public static ValidationResult Failure(string reason) => new ValidationResult { CanDeconstruct = false, Reason = reason };
  }

  /// <summary>
  /// Validates if the given structure's network can be deconstructed.
  /// </summary>
  public ValidationResult Validate(Structure structure)
  {
    if (structure == null)
      return ValidationResult.Failure("No target structure");

    return structure switch
    {
      Cable cable => ValidateCableNetwork(cable),
      Pipe pipe => ValidatePipeNetwork(pipe),
      Chute chute => ValidateChuteNetwork(chute),
      _ => ValidationResult.Failure("Unknown network type")
    };
  }

  private ValidationResult ValidateCableNetwork(Cable cable)
  {
    // Check if cable network has power using CableNetwork.PotentialLoad
    if (cable.CableNetwork != null && cable.CableNetwork.PotentialLoad > 0f)
    {
      return ValidationResult.Failure("Network is powered");
    }

    return ValidationResult.Success();
  }

  private ValidationResult ValidatePipeNetwork(Pipe pipe)
  {
    // Check if pipe has significant pressure
    if (pipe.PipeNetwork != null)
    {
      var atmosphere = pipe.PipeNetwork.Atmosphere;
      if (atmosphere != null && atmosphere.PressureGassesAndLiquidsInPa > NetworkDeconstructionConfig.SafePressureThreshold)
      {
        return ValidationResult.Failure("Network under pressure");
      }
    }

    return ValidationResult.Success();
  }

  private ValidationResult ValidateChuteNetwork(Chute chute)
  {
    // Check if chute network has items
    // TODO: Find the correct way to check for items in chutes
    // Currently allowing deconstruction as we haven't found the right API yet

    // Placeholder for future implementation:
    // if (chute has items)
    // {
    //   return ValidationResult.Failure("Network contains items");
    // }

    return ValidationResult.Success();
  }
}
