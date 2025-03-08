using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { message = "API is working", time = DateTime.UtcNow });
    }

    [HttpGet("cors")]
    [EnableCors("CorsPolicy")] // Make sure to add this attribute
    public IActionResult TestCors()
    {
        return Ok(new { message = "CORS is working", timestamp = DateTime.UtcNow });
    }
} 