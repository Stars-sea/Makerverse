using System.ComponentModel.DataAnnotations;

namespace ActivityService.Validators;

public class TagSlugValidatorAttribute : ValidationAttribute {
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) {
        bool isValid = value switch {
            string slug               => IsValidSlug(slug),
            IEnumerable<string> slugs => slugs.All(IsValidSlug),
            _                         => false
        };
        return isValid 
            ? ValidationResult.Success
            : new ValidationResult("Invalid tag slug format.");
    }

    private static bool IsValidSlug(string slug) {
        return slug.Length is >= 3 and <= 50 &&
               slug.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }

}
