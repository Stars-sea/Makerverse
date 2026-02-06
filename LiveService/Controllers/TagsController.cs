using LiveService.Data;
using LiveService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveService.Controllers;

[ApiController]
[Route("[controller]")]
public class TagsController(
    LiveDbContext db
) : ControllerBase {
    
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Tag>>> GetTags() {
        return await db.Tags.OrderBy(x => x.Name).ToListAsync();
    }
    
}
