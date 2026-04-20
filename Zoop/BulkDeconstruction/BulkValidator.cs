using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Validates if a bulk can be safely deconstructed.
/// Checks conditions like pressure, power, and item presence.
/// </summary>
public class BulkValidator
{
  /// <summary>
  /// Result of a bulk validation check.
  /// </summary>
  public class ValidationResult
  {
    public bool CanDeconstruct { get; set; }
    public string Reason { get; set; }

    public static ValidationResult Success() => new ValidationResult { CanDeconstruct = true, Reason = "" };
    public static ValidationResult Failure(string reason) => new ValidationResult { CanDeconstruct = false, Reason = reason };
  }

  /// <summary>
  /// Validates if the given structure's bulk can be deconstructed.
  /// </summary>
  public ValidationResult Validate(Structure structure)
  {
    if (structure == null)
      return ValidationResult.Failure("No target structure");

    return structure switch
    {
      Cable cable => ValidateCableBulk(cable),
      Pipe pipe => ValidatePipeBulk(pipe),
      Chute chute => ValidateChuteBulk(chute),
      _ => ValidationResult.Failure("Unknown bulk type")
    };
  }

  private ValidationResult ValidateCableBulk(Cable cable)
  {
    // Check if cable bulk has power using CableBulk.PotentialLoad
    if (cable.CableNetwork != null && cable.CableNetwork.PotentialLoad > 0f)
    {
      return ValidationResult.Failure("Bulk is powered");
    }

    return ValidationResult.Success();
  }

  private ValidationResult ValidatePipeBulk(Pipe pipe)
  {
    // Check if pipe has significant pressure
    if (pipe.PipeNetwork != null)
    {
      var atmosphere = pipe.PipeNetwork.Atmosphere;
      if (atmosphere != null && atmosphere.PressureGassesAndLiquidsInPa > BulkDeconstructionConfig.SafePressureThreshold)
      {
        return ValidationResult.Failure("Bulk under pressure");
      }
    }

    return ValidationResult.Success();
  }

  private ValidationResult ValidateChuteBulk(Chute chute)
  {
    // Check if chute bulk has items
    // TODO: Find the correct way to check for items in chutes
    // Currently allowing deconstruction as we haven't found the right API yet

    // Placeholder for future implementation:
    // if (chute has items)
    // {
    //   return ValidationResult.Failure("Bulk contains items");
    // }

    return ValidationResult.Success();
  }
}
