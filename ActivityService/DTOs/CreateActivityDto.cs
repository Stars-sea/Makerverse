using System.ComponentModel.DataAnnotations;
using ActivityService.Validators;

namespace ActivityService.DTOs;

public record CreateActivityDto(
    [Required] string Title,
    [Required] string Content,
    [Required] [TagSlugValidator] List<string> Tags,
    string? LinkedLiveId
);
