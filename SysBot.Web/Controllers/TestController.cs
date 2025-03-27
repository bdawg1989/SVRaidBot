using Microsoft.AspNetCore.Mvc;

namespace SysBot.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { message = "API funktioniert!", timestamp = DateTime.Now });
    }
    
    [HttpPost("echo")]
    public IActionResult Echo([FromBody] object data)
    {
        return Ok(new { message = "Echo erfolgreich", data, timestamp = DateTime.Now });
    }
} 