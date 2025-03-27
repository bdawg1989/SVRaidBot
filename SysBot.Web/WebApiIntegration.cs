using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SysBot.Base;
using SysBot.Pokemon;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;

namespace SysBot.Web
{
    public static class WebApiIntegration
    {
        private static Task? _serverTask;
        private static CancellationTokenSource? _cts;
        private static bool _isRunning;
        private static int _currentPort = 6500; // Standardport auf 6500 geändert
        
        // Liste aller bekannten Bot-Instanzen für das Dashboard
        private static readonly List<BotInstance> KnownInstances = new();
        
        // Repräsentiert eine Bot-Instanz
        public class BotInstance
        {
            public string Name { get; set; } = string.Empty;
            public string Host { get; set; } = "localhost";
            public int Port { get; set; }
            public bool IsActive { get; set; }
            public DateTime LastChecked { get; set; } = DateTime.Now;
            
            // Eindeutige ID für die Instanz
            public string Id => $"{Host}:{Port}";
        }

        // Gibt den Standard-Log-Dateinamen zurück
        private static string GetLogFileName()
        {
            // Wir verwenden den Standard-Log-Namen, da die SetPort-Methode im SVRaidBot nicht existiert
            return "SysBotLog.txt";
        }

        // Fügt eine Bot-Instanz zur Liste der bekannten Instanzen hinzu
        public static void AddBotInstance(string name, string host, int port)
        {
            var instance = new BotInstance
            {
                Name = name,
                Host = host,
                Port = port,
                IsActive = false,
                LastChecked = DateTime.Now
            };

            // Prüfen, ob die Instanz bereits existiert
            var existingInstance = KnownInstances.FirstOrDefault(i => i.Id == instance.Id);
            if (existingInstance != null)
            {
                existingInstance.Name = name; // Name aktualisieren
                return;
            }

            KnownInstances.Add(instance);
            LogUtil.LogInfo($"Bot-Instanz {name} ({host}:{port}) zur Liste hinzugefügt.", "WebApi");
        }

        // Entfernt eine Bot-Instanz aus der Liste der bekannten Instanzen
        public static void RemoveBotInstance(string host, int port)
        {
            var instance = KnownInstances.FirstOrDefault(i => i.Host == host && i.Port == port);
            if (instance != null)
            {
                KnownInstances.Remove(instance);
                LogUtil.LogInfo($"Bot-Instanz {instance.Name} ({host}:{port}) aus der Liste entfernt.", "WebApi");
            }
        }

        public static void StartWebApi(IPokeBotRunner runner, int port = 6500)
        {
            if (_isRunning)
                return;

            // Alle aktiven Ports abrufen, um Doppelbelegung zu vermeiden
            var activePorts = GetActiveTcpPorts();
            LogUtil.LogInfo($"Aktive TCP-Ports: {string.Join(", ", activePorts.Take(10))}{(activePorts.Count > 10 ? "..." : "")}", "WebServer");

            // Versuche verschiedene Ports, wenn der gewünschte Port belegt ist
            int attemptPort = port;
            int maxAttempts = 10; // Maximal 10 Ports versuchen (6500-6509)
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    // Wenn der Port bereits in der Liste der aktiven Ports ist, überspringen
                    if (activePorts.Contains(attemptPort))
                    {
                        LogUtil.LogInfo($"Port {attemptPort} ist laut Systemprüfung bereits belegt, versuche Port {attemptPort + 1}...", "WebServer");
                        attemptPort++;
                        continue;
                    }
                    
                    _currentPort = attemptPort;
                    // Setze den Port auch für die Log-Datei
                    // LogUtil.SetPort(_currentPort); // Diese Funktion existiert nicht in SVRaidBot
                    
                    // Prüfe, ob der Port verfügbar ist
                    if (IsPortAvailable(_currentPort))
                    {
                        // Aktuelle Instanz zur Liste hinzufügen
                        AddBotInstance($"Bot auf Port {_currentPort}", "localhost", _currentPort);
                        
                        _cts = new CancellationTokenSource();
                        var serverStartTask = Task.Run(() => 
                        {
                            try
                            {
                                RunWebServer(runner, _currentPort, _cts.Token);
                                return true;
                            }
                            catch (Exception ex)
                            {
                                if (ex.ToString().Contains("address already in use"))
                                {
                                    LogUtil.LogError($"Port {_currentPort} ist doch belegt, obwohl er als frei erkannt wurde. Versuche einen anderen Port.", "WebServer");
                                    return false;
                                }
                                throw; // Andere Fehler weiterwerfen
                            }
                        }, _cts.Token);
                        
                        // Kurz warten, um zu sehen, ob der Server wirklich startet
                        Thread.Sleep(1000);
                        
                        if (serverStartTask.IsCompleted && serverStartTask.Result == false)
                        {
                            // Server konnte nicht starten, nächsten Port versuchen
                            attemptPort++;
                            continue;
                        }
                        
                        _serverTask = serverStartTask;
                        
                        _isRunning = true;
                        
                        // Zeige den TATSÄCHLICH verwendeten Port deutlich an
                        LogUtil.LogInfo($"###### Web-API wurde ERFOLGREICH auf Port {_currentPort} gestartet ######", "WebServer");
                        return; // Erfolgreicher Start, Methode beenden
                    }
                    else
                    {
                        // Port ist belegt, versuche den nächsten
                        LogUtil.LogInfo($"Port {_currentPort} bereits belegt, versuche Port {attemptPort + 1}...", "WebServer");
                        attemptPort++;
                    }
                }
                catch (Exception ex)
                {
                    // Bei anderen Fehlern normal fortfahren
                    LogUtil.LogError($"Fehler beim Starten der Web-API: {ex.Message}", "WebServer");
                    
                    // Wenn ein Fehler beim Starten auftritt, erhöhe den Port und versuche es erneut
                    attemptPort++;
                    LogUtil.LogInfo($"Versuche Port {attemptPort}...", "WebServer");
                }
            }
            
            // Wenn wir hierher kommen, konnte kein freier Port gefunden werden
            LogUtil.LogError($"Konnte keinen freien Port im Bereich {port}-{port+maxAttempts-1} finden.", "WebServer");
        }

        public static void StopWebApi()
        {
            if (!_isRunning)
                return;

            _cts?.Cancel();
            try
            {
                _serverTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Fehler beim Stoppen des Web-Servers: {ex.Message}", "WebServer");
            }
            
            _isRunning = false;
            LogUtil.LogInfo("Web-API wurde gestoppt.", "WebServer");
        }

        private static async Task RunWebServer(IPokeBotRunner runner, int port, CancellationToken token)
        {
            try
            {
                var args = new[] { "--standalone" };
                var builder = WebApplication.CreateBuilder(args);

                // Services
                builder.Services.AddControllers();
                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
                });
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
                
                // Wichtig: Registriere den korrekten Runner-Typ
                builder.Services.AddSingleton<IPokeBotRunner>(runner);
                
                // Debug-Logging
                LogUtil.LogInfo($"Runner-Typ: {runner.GetType().Name}", "WebServer");
                
                // Typüberprüfung für den Zugriff auf die Bots-Eigenschaft
                if (runner is BotRunner<PokeBotState> pokeBotRunner)
                {
                    LogUtil.LogInfo($"Anzahl konfigurierter Bots: {pokeBotRunner.Bots.Count}", "WebServer");
                }

                // App
                var app = builder.Build();
                
                // Routing-Pipeline korrekt konfigurieren
                app.UseRouting();
                app.UseSwagger();
                app.UseSwaggerUI();
                app.UseCors();
                
                // Einfache Willkommensseite
                app.MapGet("/", () => "SysBot.Pokemon API - Verbindung hergestellt. Verwenden Sie ein separates Dashboard, um auf diese API zuzugreifen.");
                
                // Einfache Test-API direkt hinzufügen
                app.MapGet("/api/healthcheck", () => new { status = "ok", timestamp = DateTime.Now.ToString() });
                
                // API für Instanzen-Management
                app.MapGet("/api/instances", () => Results.Ok(KnownInstances));
                
                app.MapGet("/api/instances/check", () => 
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(2);
                    
                    foreach (var instance in KnownInstances)
                    {
                        try
                        {
                            var response = client.GetAsync($"http://{instance.Host}:{instance.Port}/api/healthcheck").Result;
                            instance.IsActive = response.IsSuccessStatusCode;
                        }
                        catch
                        {
                            instance.IsActive = false;
                        }
                        instance.LastChecked = DateTime.Now;
                    }
                    
                    return Results.Ok(KnownInstances);
                });
                
                app.MapPost("/api/instances/add", async (HttpContext context) =>
                {
                    try
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var json = System.Text.Json.JsonDocument.Parse(body);
                        
                        var name = json.RootElement.GetProperty("name").GetString() ?? "Neue Instanz";
                        var host = json.RootElement.GetProperty("host").GetString() ?? "localhost";
                        var port = json.RootElement.GetProperty("port").GetInt32();
                        
                        AddBotInstance(name, host, port);
                        return Results.Ok(new { success = true, message = "Instanz hinzugefügt" });
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest(new { success = false, message = $"Fehler: {ex.Message}" });
                    }
                });
                
                app.MapPost("/api/instances/remove", async (HttpContext context) =>
                {
                    try
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var json = System.Text.Json.JsonDocument.Parse(body);
                        
                        var host = json.RootElement.GetProperty("host").GetString() ?? "localhost";
                        var port = json.RootElement.GetProperty("port").GetInt32();
                        
                        RemoveBotInstance(host, port);
                        return Results.Ok(new { success = true, message = "Instanz entfernt" });
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest(new { success = false, message = $"Fehler: {ex.Message}" });
                    }
                });
                
                // Sicherer Zugriff auf Bot-Informationen
                app.MapGet("/api/botinfo", () => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        return new { count = pokeBotRunner.Bots.Count, isRunning = pokeBotRunner.IsRunning };
                    }
                    return new { count = 0, isRunning = false };
                });
                
                // Direkte API-Endpunkte für Bot-Steuerung
                // GET: Alle Bots abrufen
                app.MapGet("/api/bots", () => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        var bots = pokeBotRunner.Bots.Select(z => new
                        {
                            id = z.Bot.Connection.Name,
                            name = z.Bot.Connection.Name,
                            running = z.IsRunning,
                            paused = z.IsPaused,
                            routine = z.Bot.Config.CurrentRoutineType.ToString(),
                            lastTime = z.Bot.LastTime.ToString("HH:mm:ss"),
                            lastLogged = z.Bot.LastLogged
                        });
                        return Results.Ok(bots);
                    }
                    return Results.Ok(Array.Empty<object>());
                });

                // API für Logs abrufen
                app.MapGet("/api/logs", (HttpContext context) => 
                {
                    try
                    {
                        // Direkter Log-Dateiname statt LogUtil.GetLogFileName()
                        var logFileName = "SysBotLog.txt";
                        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", logFileName);
                        
                        // Konvertiere den QueryString-Parameter
                        int lines = 100;
                        if (context.Request.Query.TryGetValue("lines", out var linesValue))
                        {
                            int.TryParse(linesValue, out lines);
                        }

                        if (!System.IO.File.Exists(logPath))
                        {
                            LogUtil.LogInfo($"Log-Datei nicht gefunden: {logPath}", "WebApi");
                            return Results.Ok(new string[] { $"Keine Logs verfügbar. Log-Datei nicht gefunden: {logFileName}" });
                        }

                        // File.ReadLines kann nicht verwendet werden, wenn die Datei gesperrt ist
                        // Stattdessen FileStream mit FileShare.ReadWrite verwenden
                        List<string> logLines = new List<string>();
                        try
                        {
                            using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                // Alle Zeilen in einen Puffer lesen
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    logLines.Add(line);
                                }
                            }

                            // Die letzten 'lines' Zeilen zurückgeben
                            if (logLines.Count > lines)
                            {
                                logLines = logLines.Skip(logLines.Count - lines).ToList();
                            }
                        }
                        catch (IOException ex)
                        {
                            LogUtil.LogError($"Zugriffsfehler beim Lesen der Logs: {ex.Message}", "WebApi");
                            return Results.Ok(new string[] { 
                                $"Log-Datei {logFileName} kann nicht gelesen werden (wird von einem anderen Prozess verwendet).",
                                "Bitte versuchen Sie es später erneut." 
                            });
                        }
                        
                        if (logLines.Count == 0)
                        {
                            return Results.Ok(new string[] { $"Log-Datei {logFileName} ist leer." });
                        }
                        
                        return Results.Ok(logLines);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Fehler beim Lesen der Logs: {ex.Message}", "WebApi");
                        return Results.Ok(new string[] { $"Fehler beim Abrufen der Logs: {ex.Message}" });
                    }
                });

                // POST: Bot starten
                app.MapPost("/api/bots/{id}/start", (string id) => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        var bot = pokeBotRunner.GetBot(id);
                        if (bot == null)
                            return Results.NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

                        try
                        {
                            pokeBotRunner.InitializeStart();
                            bot.Start();
                            return Results.Ok(new { success = true, message = "Bot gestartet" });
                        }
                        catch (Exception ex)
                        {
                            return Results.BadRequest(new { success = false, message = $"Fehler beim Starten: {ex.Message}" });
                        }
                    }
                    return Results.BadRequest(new { success = false, message = "Bot-Runner nicht verfügbar" });
                });

                // POST: Bot stoppen
                app.MapPost("/api/bots/{id}/stop", (string id) => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        var bot = pokeBotRunner.GetBot(id);
                        if (bot == null)
                            return Results.NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

                        try
                        {
                            bot.Stop();
                            return Results.Ok(new { success = true, message = "Bot gestoppt" });
                        }
                        catch (Exception ex)
                        {
                            return Results.BadRequest(new { success = false, message = $"Fehler beim Stoppen: {ex.Message}" });
                        }
                    }
                    return Results.BadRequest(new { success = false, message = "Bot-Runner nicht verfügbar" });
                });

                // POST: Bot neustarten und stoppen
                app.MapPost("/api/bots/{id}/rebootAndStop", (string id) => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        var bot = pokeBotRunner.GetBot(id);
                        if (bot == null)
                            return Results.NotFound(new { message = $"Kein Bot mit der ID {id} gefunden." });

                        try
                        {
                            // Je nach Implementierung - hier vereinfacht
                            bot.Stop();
                            // Kurze Wartezeit
                            Thread.Sleep(1000);
                            bot.Bot.Connection.Reset();
                            LogUtil.LogInfo($"Bot {id} wurde neu gestartet und gestoppt", "WebApi");
                            return Results.Ok(new { success = true, message = "Bot neu gestartet und gestoppt" });
                        }
                        catch (Exception ex)
                        {
                            return Results.BadRequest(new { success = false, message = $"Fehler beim Neustart: {ex.Message}" });
                        }
                    }
                    return Results.BadRequest(new { success = false, message = "Bot-Runner nicht verfügbar" });
                });
                
                // POST: Alle Bots starten
                app.MapPost("/api/bots/startAll", () => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        try
                        {
                            pokeBotRunner.InitializeStart();
                            pokeBotRunner.StartAll();
                            return Results.Ok(new { success = true, message = "Alle Bots gestartet" });
                        }
                        catch (Exception ex)
                        {
                            return Results.BadRequest(new { success = false, message = $"Fehler beim Starten aller Bots: {ex.Message}" });
                        }
                    }
                    return Results.BadRequest(new { success = false, message = "Bot-Runner nicht verfügbar" });
                });

                // POST: Alle Bots stoppen
                app.MapPost("/api/bots/stopAll", () => 
                {
                    if (runner is BotRunner<PokeBotState> pokeBotRunner)
                    {
                        try
                        {
                            pokeBotRunner.StopAll();
                            return Results.Ok(new { success = true, message = "Alle Bots gestoppt" });
                        }
                        catch (Exception ex)
                        {
                            return Results.BadRequest(new { success = false, message = $"Fehler beim Stoppen aller Bots: {ex.Message}" });
                        }
                    }
                    return Results.BadRequest(new { success = false, message = "Bot-Runner nicht verfügbar" });
                });
                
                // Controller-Endpunkte (WICHTIG: Erst NACH den anderen Middleware-Komponenten)
                app.MapControllers();

                // Server in einem separaten Thread starten
                var serverTask = Task.Run(async () =>
                {
                    try
                    {
                        // URLs konfigurieren und Server starten
                        app.Urls.Clear();
                        // Wir binden nur an IPv4 localhost und nicht an alle IP-Adressen (*)
                        // Dies vermeidet Konflikte mit IPv6 und anderen Netzwerkschnittstellen
                        app.Urls.Add($"http://127.0.0.1:{port}");
                        // Wir fügen IPv6 nur hinzu, wenn es unterstützt wird
                        if (System.Net.Sockets.Socket.OSSupportsIPv6)
                        {
                            app.Urls.Add($"http://[::1]:{port}");
                        }
                        
                        LogUtil.LogInfo("Starte Web-Server...", "WebServer");
                        await app.StartAsync(token);
                        LogUtil.LogInfo("Web-Server erfolgreich gestartet.", "WebServer");
                        
                        // Warte bis zum Token-Cancel
                        await Task.Delay(Timeout.Infinite, token);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Fehler beim Starten des Web-Servers: {ex.Message}", "WebServer");
                        LogUtil.LogError($"Stack-Trace: {ex.StackTrace}", "WebServer");
                        if (ex.InnerException != null)
                            LogUtil.LogError($"Inner-Exception: {ex.InnerException.Message}", "WebServer");
                    }
                }, token);

                // Warte auf Abbruch
                await serverTask;
            }
            catch (OperationCanceledException)
            {
                // Normal bei Abbruch
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Fehler im Web-Server: {ex.Message}", "WebServer");
                LogUtil.LogError($"Stack-Trace: {ex.StackTrace}", "WebServer");
                if (ex.InnerException != null)
                    LogUtil.LogError($"Inner-Exception: {ex.InnerException.Message}", "WebServer");
            }
        }

        // Überprüft, ob ein Port verfügbar ist
        private static bool IsPortAvailable(int port)
        {
            // Prüfe sowohl IPv4 als auch IPv6
            bool isAvailable = true;
            
            // Prüfe IPv4
            try
            {
                using var tcpListener = new TcpListener(System.Net.IPAddress.Parse("127.0.0.1"), port);
                tcpListener.Start();
                tcpListener.Stop();
            }
            catch
            {
                isAvailable = false;
            }
            
            // Auch wenn IPv4 frei ist, könnte IPv6 belegt sein
            if (isAvailable && System.Net.Sockets.Socket.OSSupportsIPv6)
            {
                try
                {
                    using var tcpListenerV6 = new TcpListener(System.Net.IPAddress.IPv6Loopback, port);
                    tcpListenerV6.Start();
                    tcpListenerV6.Stop();
                }
                catch
                {
                    isAvailable = false;
                }
            }
            
            return isAvailable;
        }

        // Gibt alle aktiven TCP-Ports zurück
        private static List<int> GetActiveTcpPorts()
        {
            var result = new List<int>();
            try
            {
                // Verwende IPGlobalProperties, um aktive TCP-Verbindungen und -Listener zu erhalten
                var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                
                // Aktive TCP-Verbindungen
                var connections = properties.GetActiveTcpConnections();
                foreach (var connection in connections)
                {
                    if (!result.Contains(connection.LocalEndPoint.Port))
                        result.Add(connection.LocalEndPoint.Port);
                }
                
                // TCP-Listener
                var listeners = properties.GetActiveTcpListeners();
                foreach (var listener in listeners)
                {
                    if (!result.Contains(listener.Port))
                        result.Add(listener.Port);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Fehler beim Abrufen der aktiven TCP-Ports: {ex.Message}", "WebServer");
            }
            
            return result;
        }
    }
} 