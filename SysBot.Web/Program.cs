using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SysBot.Base;
using SysBot.Pokemon;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Konfiguriere CORS, um Anfragen vom Linux-Server zuzulassen
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// SysBot Singleton-Instanz registrieren
// Diese muss in der WinForms-App initialisiert werden
// Dummy für die Projektstruktur - wird zur Laufzeit überschrieben
builder.Services.AddSingleton<IPokeBotRunner>(sp => 
{
    #pragma warning disable CS8603 // Possible null reference return
    return null;
    #pragma warning restore CS8603 // Possible null reference return
});

var app = builder.Build();

// Konfiguriere die HTTP-Pipeline in der richtigen Reihenfolge
app.UseRouting(); // Routing zuerst

// CORS aktivieren
app.UseCors();

// Statische Dateien aktivieren
app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger immer aktivieren, nicht nur im Development-Modus
app.UseSwagger();
app.UseSwaggerUI();

// HTTPS-Weiterleitung falls nötig
app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Alle Controller-Endpunkte registrieren (NACH allen UseXXX-Aufrufen)
app.MapControllers();

// Testendpunkt zur Diagnose direkt definieren
app.MapGet("/api/healthcheck", () => new { status = "ok", timestamp = DateTime.Now });

// Starte die Anwendung
if (args.Length > 0 && args[0] == "--standalone")
{
    // Direkter Start nur für Entwicklungszwecke
    app.Run("http://*:6500");
}
else
{
    // Normaler Start, wird vom Hauptprogramm weiter konfiguriert
    app.Run();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
