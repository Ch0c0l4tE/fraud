using FluentValidation;
using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Validators;

/// <summary>
/// Validator for CreateSessionRequest
/// </summary>
public class CreateSessionRequestValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionRequestValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty()
            .WithMessage("ClientId is required")
            .MaximumLength(256)
            .WithMessage("ClientId must not exceed 256 characters");

        RuleFor(x => x.DeviceFingerprint)
            .NotEmpty()
            .WithMessage("DeviceFingerprint is required")
            .MaximumLength(512)
            .WithMessage("DeviceFingerprint must not exceed 512 characters");
    }
}

/// <summary>
/// Validator for SignalDto
/// </summary>
public class SignalDtoValidator : AbstractValidator<SignalDto>
{
    private static readonly HashSet<string> ValidSignalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "mouse_move", "mousemove",
        "mouse_click", "mouseclick", "click",
        "keystroke",
        "keystroke_dynamics", "keystrokedynamics",
        "scroll",
        "touch",
        "visibility",
        "focus",
        "paste",
        "device",
        "performance",
        "fingerprint",
        "form_interaction", "forminteraction",
        "accelerometer",
        "gyroscope",
        "app_lifecycle", "applifecycle",
        "jailbreak_detection", "jailbreakdetection",
        "root_detection", "rootdetection"
    };

    public SignalDtoValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .WithMessage("Signal type is required")
            .Must(type => ValidSignalTypes.Contains(type.Replace("_", "").ToLowerInvariant()) || ValidSignalTypes.Contains(type))
            .WithMessage(x => $"Invalid signal type: {x.Type}");

        RuleFor(x => x.Timestamp)
            .GreaterThan(0)
            .WithMessage("Timestamp must be a positive value");

        RuleFor(x => x.Payload)
            .NotNull()
            .WithMessage("Payload is required");
    }
}

/// <summary>
/// Validator for AppendSignalsRequest
/// </summary>
public class AppendSignalsRequestValidator : AbstractValidator<AppendSignalsRequest>
{
    public AppendSignalsRequestValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("SessionId is required");

        RuleFor(x => x.Signals)
            .NotNull()
            .WithMessage("Signals list is required")
            .NotEmpty()
            .WithMessage("At least one signal is required")
            .Must(signals => signals.Count <= 1000)
            .WithMessage("Maximum 1000 signals per request");

        RuleForEach(x => x.Signals)
            .SetValidator(new SignalDtoValidator());
    }
}
