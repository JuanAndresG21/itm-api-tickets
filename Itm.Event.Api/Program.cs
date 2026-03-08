using Itm.Event.Api.Constants;
using Itm.Event.Api.Dtos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
app.MapGet("/api/events/{id}", (int id) =>
{
    var ev = events.FirstOrDefault(e => e.Id == id);
    if (ev is null)
        return Results.NotFound($"Evento con Id {id} no encontrado.");

    return Results.Ok(new EventDto(ev.Id, ev.Name, ev.BasePrice, ev.AvailableSeats));
})
.WithName("GetEvent");

// -----------------------------------------------------------
// POST /api/events/reserve — Reservar sillas (Paso 1 de SAGA)
// -----------------------------------------------------------
// Si no hay sillas suficientes retorna 400 BadRequest.
app.MapPost("/api/events/reserve", (ReservationDto request) =>
{
    lock (lockObj)
    {
        var ev = events.FirstOrDefault(e => e.Id == request.EventId);
        if (ev is null)
            return Results.NotFound($"Evento con Id {request.EventId} no encontrado.");

        if (ev.AvailableSeats < request.Quantity)
            return Results.BadRequest(
                $"Sillas insuficientes. Solicitadas: {request.Quantity}, Disponibles: {ev.AvailableSeats}.");

        ev.AvailableSeats -= request.Quantity;

        Console.WriteLine($"[RESERVE] Evento {ev.Id}: -{request.Quantity} sillas. Quedan: {ev.AvailableSeats}");

        return Results.Ok(new
        {
            Message = "Sillas reservadas exitosamente.",
            RemainingSeats = ev.AvailableSeats
        });
    }
})
.WithName("ReserveSeats");

// -----------------------------------------------------------
// POST /api/events/release — Liberar sillas (Compensación SAGA / Ctrl+Z)
// -----------------------------------------------------------
app.MapPost("/api/events/release", (ReservationDto request) =>
{
    lock (lockObj)
    {
        var ev = events.FirstOrDefault(e => e.Id == request.EventId);
        if (ev is null)
            return Results.NotFound($"Evento con Id {request.EventId} no encontrado.");

        ev.AvailableSeats += request.Quantity;

        Console.WriteLine($"[RELEASE] Evento {ev.Id}: +{request.Quantity} sillas. Quedan: {ev.AvailableSeats}");

        return Results.Ok(new
        {
            Message = "Sillas liberadas exitosamente.",
            RemainingSeats = ev.AvailableSeats
        });
    }
})
.WithName("ReleaseSeats");

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
