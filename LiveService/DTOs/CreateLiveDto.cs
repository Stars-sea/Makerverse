using System.ComponentModel.DataAnnotations;

namespace LiveService.DTOs;

public record CreateLiveDto(
    [Required] string Title,
    [Required] List<string> Tags
);
