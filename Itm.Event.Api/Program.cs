using Itm.Event.Api.Constants;
using Itm.Event.Api.Dtos;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnection) || redisConnection == "REDIS_CONNECTION")
{
    redisConnection = "localhost:6379";
}

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "itm-events:";
});

var app = builder.Build();

// Muestra en consola la URL efectiva al arrancar (env var > appsettings > default)
var resolvedUrl = ServiceUrls.EventApiBaseUrl(app.Configuration);
app.Logger.LogInformation("[Config] Event.Api URL: {Url}", resolvedUrl);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// -----------------------------------------------------------
// BASE DE DATOS SIMULADA EN MEMORIA
// -----------------------------------------------------------
// En producción esto vendería de una BD real (EF Core, Dapper, etc.)
var events = new List<EventModel>
{
    new() { Id = 1, Name = "Concierto ITM", BasePrice = 50_000m, AvailableSeats = 100 }
};

// Lock para garantizar que dos peticiones simultáneas no causen sobreventa
var lockObj = new object();

// -----------------------------------------------------------
// GET /api/events/{id} — Consultar información del evento
// -----------------------------------------------------------
app.MapGet("/api/events/{id}", async (int id, IDistributedCache cache) =>
{
    var cacheKey = $"event:{id}";
    var cached = await cache.GetStringAsync(cacheKey);
    if (!string.IsNullOrWhiteSpace(cached))
    {
        var cachedDto = JsonSerializer.Deserialize<EventDto>(cached);
        if (cachedDto is not null)
            return Results.Ok(cachedDto);
    }

    var ev = events.FirstOrDefault(e => e.Id == id);
    if (ev is null)
        return Results.NotFound($"Evento con Id {id} no encontrado.");

    var dto = new EventDto(ev.Id, ev.Name, ev.BasePrice, ev.AvailableSeats);
    await cache.SetStringAsync(
        cacheKey,
        JsonSerializer.Serialize(dto),
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        });

    return Results.Ok(dto);
})
.WithName("GetEvent");

// -----------------------------------------------------------
// POST /api/events/reserve — Reservar sillas (Paso 1 de SAGA)
// -----------------------------------------------------------
// Si no hay sillas suficientes retorna 400 BadRequest.
app.MapPost("/api/events/reserve", async (ReservationDto request, IDistributedCache cache) =>
{
    bool notFound = false;
    string? errorMessage = null;
    var remainingSeats = 0;

    lock (lockObj)
    {
        var ev = events.FirstOrDefault(e => e.Id == request.EventId);
        if (ev is null)
        {
            notFound = true;
            errorMessage = $"Evento con Id {request.EventId} no encontrado.";
        }
        else if (ev.AvailableSeats < request.Quantity)
        {
            errorMessage = $"Sillas insuficientes. Solicitadas: {request.Quantity}, Disponibles: {ev.AvailableSeats}.";
        }
        else
        {
            ev.AvailableSeats -= request.Quantity;
            remainingSeats = ev.AvailableSeats;
        }
    }

    if (notFound)
        return Results.NotFound(errorMessage);

    if (errorMessage is not null)
        return Results.BadRequest(errorMessage);

    await cache.RemoveAsync($"event:{request.EventId}");

    Console.WriteLine($"[RESERVE] Evento {request.EventId}: -{request.Quantity} sillas. Quedan: {remainingSeats}");

    return Results.Ok(new
    {
        Message = "Sillas reservadas exitosamente.",
        RemainingSeats = remainingSeats
    });
})
.WithName("ReserveSeats");

// -----------------------------------------------------------
// POST /api/events/release — Liberar sillas (Compensación SAGA / Ctrl+Z)
// -----------------------------------------------------------
app.MapPost("/api/events/release", async (ReservationDto request, IDistributedCache cache) =>
{
    bool notFound = false;
    var remainingSeats = 0;

    lock (lockObj)
    {
        var ev = events.FirstOrDefault(e => e.Id == request.EventId);
        if (ev is null)
        {
            notFound = true;
        }
        else
        {
            ev.AvailableSeats += request.Quantity;
            remainingSeats = ev.AvailableSeats;
        }
    }

    if (notFound)
        return Results.NotFound($"Evento con Id {request.EventId} no encontrado.");

    await cache.RemoveAsync($"event:{request.EventId}");

    Console.WriteLine($"[RELEASE] Evento {request.EventId}: +{request.Quantity} sillas. Quedan: {remainingSeats}");

    return Results.Ok(new
    {
        Message = "Sillas liberadas exitosamente.",
        RemainingSeats = remainingSeats
    });
})
.WithName("ReleaseSeats");

app.MapGet("/health", () => Results.Ok("OK"));

app.Run();

// -----------------------------------------------------------
// MODELO INTERNO (no se expone directamente al cliente)
// -----------------------------------------------------------
class EventModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public int AvailableSeats { get; set; }
}
