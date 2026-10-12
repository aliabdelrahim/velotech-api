using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velotech.API.Data;

namespace Velotech.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class RolesController : ControllerBase
{
    private readonly VelotechDbContext _db;

    public RolesController(VelotechDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Liste les roles disponibles (Admin only).
    /// Utilise par le back-office pour peupler le select du formulaire user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<object>>> GetRoles()
    {
        var roles = await _db.Roles
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();

        return Ok(roles);
    }
}
