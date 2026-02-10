using System.ComponentModel.DataAnnotations;

namespace AuthService.DTOs;

public record RegisterDto(
    [Required] string Username,
    [Required] [EmailAddress] string Email,
    [Required] string Password
);
