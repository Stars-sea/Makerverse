using AuthService.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public class AccountDbContext(DbContextOptions options) : IdentityDbContext<ApplicationUser>(options);
