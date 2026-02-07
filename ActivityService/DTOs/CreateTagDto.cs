using System.ComponentModel.DataAnnotations;
using ActivityService.Validators;

namespace ActivityService.DTOs;

public record CreateTagDto(
    [Required] string Name,
    [Required] [TagSlugValidator] string Slug,
    [Required] string Description
);
