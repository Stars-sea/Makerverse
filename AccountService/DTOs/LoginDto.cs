using System.ComponentModel.DataAnnotations;

namespace AuthService.DTOs;

public record LoginDto(
    [Required] string Username,
    [Required] string Password
);
