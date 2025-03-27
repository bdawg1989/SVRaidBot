using Microsoft.AspNetCore.Mvc;
using SysBot.Base;
using SysBot.Pokemon;
using System.IO;
using System.Linq;

namespace SysBot.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BotsController : ControllerBase
{
    private readonly BotRunner<PokeBotState> _runner;

    public BotsController(IPokeBotRunner runner)
    {
        _runner = (BotRunner<PokeBotState>)runner;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var bots = _runner.Bots.Select(z => new
        {
            id = z.Bot.Connection.Name,
            name = z.Bot.Connection.Name,
            running = z.IsRunning,
            paused = z.IsPaused,
            routine = z.Bot.Config.CurrentRoutineType.ToString(),
            lastTime = z.Bot.LastTime.ToString("HH:mm:ss"),
            lastLogged = z.Bot.LastLogged
        });

        return Ok(bots);
    }

    [HttpPost("{id}/start")]
    public IActionResult StartBot(string id)
    {
        var bot = _runner.GetBot(id);
        if (bot == null)
            return NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

        try
        {
            var pokeBotRunner = (IPokeBotRunner)_runner;
            if (!pokeBotRunner.Config.SkipConsoleBotCreation)
            {
                pokeBotRunner.InitializeStart();
                bot.Start();
                return Ok(new { success = true, message = "Bot gestartet" });
            }
            return BadRequest(new { success = false, message = "SkipConsoleBotCreation ist aktiviert, Start nicht möglich" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Starten: {ex.Message}" });
        }
    }

    [HttpPost("{id}/stop")]
    public IActionResult StopBot(string id)
    {
        var bot = _runner.GetBot(id);
        if (bot == null)
            return NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

        try
        {
            bot.Stop();
            return Ok(new { success = true, message = "Bot gestoppt" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Stoppen: {ex.Message}" });
        }
    }

    [HttpPost("{id}/idle")]
    public IActionResult IdleBot(string id)
    {
        var bot = _runner.GetBot(id);
        if (bot == null)
            return NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

        try
        {
            bot.Pause();
            return Ok(new { success = true, message = "Bot pausiert" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Pausieren: {ex.Message}" });
        }
    }

    [HttpPost("{id}/resume")]
    public IActionResult ResumeBot(string id)
    {
        var bot = _runner.GetBot(id);
        if (bot == null)
            return NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

        try
        {
            bot.Resume();
            return Ok(new { success = true, message = "Bot fortgesetzt" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Fortsetzen: {ex.Message}" });
        }
    }

    [HttpPost("{id}/rebootAndStop")]
    public IActionResult RebootAndStopBot(string id)
    {
        var bot = _runner.GetBot(id);
        if (bot == null)
            return NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

        try
        {
            var pokeBotRunner = (IPokeBotRunner)_runner;
            if (!pokeBotRunner.Config.SkipConsoleBotCreation)
            {
                bot.Stop();
                bot.Bot.Connection.Reset();
                return Ok(new { success = true, message = "Bot neu gestartet und gestoppt" });
            }
            return BadRequest(new { success = false, message = "SkipConsoleBotCreation ist aktiviert, Neustart nicht möglich" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Neustart: {ex.Message}" });
        }
    }

    [HttpPost("startAll")]
    public IActionResult StartAllBots()
    {
        try
        {
            var pokeBotRunner = (IPokeBotRunner)_runner;
            if (!pokeBotRunner.Config.SkipConsoleBotCreation)
            {
                pokeBotRunner.StartAll();
                return Ok(new { success = true, message = "Alle Bots gestartet" });
            }
            return BadRequest(new { success = false, message = "SkipConsoleBotCreation ist aktiviert, Start nicht möglich" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Starten aller Bots: {ex.Message}" });
        }
    }

    [HttpPost("stopAll")]
    public IActionResult StopAllBots()
    {
        try
        {
            var pokeBotRunner = (IPokeBotRunner)_runner;
            if (!pokeBotRunner.Config.SkipConsoleBotCreation)
            {
                pokeBotRunner.StopAll();
                return Ok(new { success = true, message = "Alle Bots gestoppt" });
            }
            return BadRequest(new { success = false, message = "SkipConsoleBotCreation ist aktiviert, Stopp nicht möglich" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Fehler beim Stoppen aller Bots: {ex.Message}" });
        }
    }

    [HttpGet("logs")]
    public IActionResult GetLogs(int lines = 100)
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
} 