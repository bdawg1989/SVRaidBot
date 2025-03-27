using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Linq;

namespace SysBot.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogController : ControllerBase
{
    [HttpGet]
    public IActionResult GetLogs(int lines = 100)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "SysBotLog.txt");
        if (!System.IO.File.Exists(logPath))
            return NotFound(new { message = "Log-Datei nicht gefunden." });

        var logLines = System.IO.File.ReadLines(logPath)
                         .TakeLast(lines)
                         .ToList();
        return Ok(logLines);
    }

    [HttpGet("files")]
    public IActionResult GetLogFiles()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logPath))
            return NotFound(new { message = "Log-Verzeichnis nicht gefunden." });

        var files = Directory.GetFiles(logPath, "*.txt")
                    .Select(f => new
                    {
                        Name = Path.GetFileName(f),
                        Size = new FileInfo(f).Length,
                        LastModified = new FileInfo(f).LastWriteTime
                    })
                    .OrderByDescending(f => f.LastModified)
                    .ToList();

        return Ok(files);
    }

    [HttpGet("file/{fileName}")]
    public IActionResult GetLogFile(string fileName, int lines = 100)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", fileName);
        if (!System.IO.File.Exists(logPath))
            return NotFound(new { message = $"Log-Datei {fileName} nicht gefunden." });

        var logLines = System.IO.File.ReadLines(logPath)
                         .TakeLast(lines)
                         .ToList();
        return Ok(logLines);
    }
} 