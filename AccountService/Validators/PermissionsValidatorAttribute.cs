using System.ComponentModel.DataAnnotations;
using System.Reflection;
using OpenIddict.Abstractions;

namespace AuthService.Validators;

public class PermissionsValidatorAttribute : ValidationAttribute {

    private static readonly HashSet<string> ValidPermissions = GetValidPermissions();

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) {
        if (value is null) return ValidationResult.Success;

        if (value is not IEnumerable<string> permissions)
            return new ValidationResult("The permissions must be a collection of strings.");

        return permissions.Any(permission => !ValidPermissions.Contains(permission))
            ? new ValidationResult("One or more permissions are invalid.")
            : ValidationResult.Success;
    }

    private static HashSet<string> GetValidPermissions() {
        HashSet<string> permissions     = new(StringComparer.Ordinal);
        Type            permissionsType = typeof(OpenIddictConstants.Permissions);

        foreach (FieldInfo field in permissionsType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)) {
            if (field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string)) {
                permissions.Add((string)field.GetValue(null)!);
            }
        }

        foreach (Type nestedType in permissionsType.GetNestedTypes(BindingFlags.Public)) {
            foreach (FieldInfo field in nestedType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)) {
                if (field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string)) {
                    permissions.Add((string)field.GetValue(null)!);
                }
            }
        }

        return permissions;
    }

}
