using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Linq;

namespace SysBot.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get(int lines = 100)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "SysBotLog.txt");
            if (!System.IO.File.Exists(logPath))
                return NotFound(new { success = false, message = "Log-Datei nicht gefunden." });

            var logLines = System.IO.File.ReadLines(logPath)
                            .TakeLast(lines)
                            .ToList();
            return Ok(logLines);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Abrufen der Logs: {ex.Message}" });
        }
    }

    [HttpGet("latest")]
    public IActionResult GetLatest(int count = 10)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "SysBotLog.txt");
            if (!System.IO.File.Exists(logPath))
                return NotFound(new { success = false, message = "Log-Datei nicht gefunden." });

            var logLines = System.IO.File.ReadLines(logPath)
                            .TakeLast(count)
                            .ToList();
            return Ok(logLines);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Abrufen der Logs: {ex.Message}" });
        }
    }
} 