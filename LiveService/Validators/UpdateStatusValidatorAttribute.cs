using System.ComponentModel.DataAnnotations;

namespace LiveService.Validators;

public class UpdateStatusValidatorAttribute : ValidationAttribute {

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) {
        if (value is not string status || status != "start" && status != "stop") {
            return new ValidationResult("Status must be 'start' / 'stop'.");
        }

        return ValidationResult.Success;
    }
}
