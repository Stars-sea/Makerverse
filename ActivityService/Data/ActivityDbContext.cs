using ActivityService.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Data;

public class ActivityDbContext(DbContextOptions options) : DbContext(options) {

    public DbSet<Activity> Activities { get; set; }
    
    public DbSet<Comment> Comments { get; set; }

    public DbSet<Tag> Tags { get; set; }

}
