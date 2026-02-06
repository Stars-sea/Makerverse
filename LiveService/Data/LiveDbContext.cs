using LiveService.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveService.Data;

public class LiveDbContext(DbContextOptions options) : DbContext(options) {
    
    public DbSet<Live> Lives { get; set; }
    
    public DbSet<Tag> Tags { get; set; }

}
