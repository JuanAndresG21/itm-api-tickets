using Itm.Booking.Api.Constants;
using Itm.Booking.Api.Dtos;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -----------------------------------------------------------
// REGISTRO DE CLIENTES HTTP CON RESILIENCIA (Rúbrica Nivel 5)
// -----------------------------------------------------------
// .AddStandardResilienceHandler() agrega automáticamente:
//   - Reintentos (Retry): reintenta hasta 3 veces si falla.
//   - Circuit Breaker: deja de intentar si el servicio falla repetidamente.
//   - Timeout: cancela si la respuesta tarda demasiado.
var config = builder.Configuration;

builder.Services
    .AddHttpClient("EventClient", client =>
    {
        client.BaseAddress = new Uri(ServiceUrls.EventApiBaseUrl(config));
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler();

builder.Services
    .AddHttpClient("DiscountClient", client =>
    {
        client.BaseAddress = new Uri(ServiceUrls.DiscountApiBaseUrl(config));
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// -----------------------------------------------------------
// POST /api/bookings — Orquestador principal (Patrón SAGA)
// -----------------------------------------------------------
app.MapPost("/api/bookings", async (BookingRequestDto request, IHttpClientFactory factory) =>
{
    var eventClient    = factory.CreateClient("EventClient");
    var discountClient = factory.CreateClient("DiscountClient");

    // -------------------------------------------------------
    // PASO 1: LECTURA EN PARALELO (FASE 2 — Clase 2)
    // Task.WhenAll consulta Event y Discount al mismo tiempo.
    // -------------------------------------------------------
    var eventTask    = eventClient.GetFromJsonAsync<EventDto>($"/api/events/{request.EventId}");
    var discountTask = discountClient.GetAsync($"/api/discounts/{request.DiscountCode}");

    try
    {
        await Task.WhenAll(eventTask, discountTask);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        // El evento no existe (Event.Api respondió 404)
        return Results.NotFound($"El evento con Id {request.EventId} no fue encontrado.");
    }
    catch (HttpRequestException ex)
    {
        // Error de red: el servicio está caído, timeout, connection refused, etc.
        return Results.Problem(
            detail:     $"Un servicio dependiente no está disponible: {ex.Message}",
            title:      "Servicio no disponible",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var eventData = await eventTask;
    if (eventData is null)
        return Results.NotFound("El evento no fue encontrado.");

    // El descuento es opcional: si el código no existe (404) el porcentaje es 0.
    decimal discountPercentage = 0m;
    var discountResponse = await discountTask;
    if (discountResponse.IsSuccessStatusCode)
    {
        var discountData = await discountResponse.Content.ReadFromJsonAsync<DiscountDto>();
        discountPercentage = discountData?.Percentage ?? 0m;
    }

    // -------------------------------------------------------
    // PASO 2: MATEMÁTICAS — Calcular total a pagar
    // Total = (PrecioBase * Cantidad) - Descuento
    // -------------------------------------------------------
    var subtotal = eventData.BasePrice * request.Tickets;
    var discount = subtotal * discountPercentage;
    var total    = subtotal - discount;

    // -------------------------------------------------------
    // PASO 3 (INICIO SAGA): RESERVAR SILLAS en Event.Api
    // -------------------------------------------------------
    var reserveResponse = await eventClient.PostAsJsonAsync("/api/events/reserve",
        new ReservationDto(request.EventId, request.Tickets));

    if (!reserveResponse.IsSuccessStatusCode)
        return Results.BadRequest("No hay sillas suficientes o el evento no existe.");

    try
    {
        // -------------------------------------------------------
        // PASO 4: SIMULACIÓN DE PAGO
        // Random 1-10: > 5 = éxito, <= 5 = falla (Clase 3)
        // -------------------------------------------------------
        var paymentSuccess = new Random().Next(1, 11) > 5;
        if (!paymentSuccess)
            throw new Exception("Fondos insuficientes en la tarjeta de crédito.");

        // Pago exitoso: retornar factura
        return Results.Ok(new BookingInvoiceDto(
            Status:       "Éxito",
            EventName:    eventData.Name,
            Tickets:      request.Tickets,
            Subtotal:     subtotal,
            Discount:     discount,
            Total:        total,
            Message:      "¡Disfruta el concierto ITM!"
        ));
    }
    catch (Exception ex)
    {
        // -------------------------------------------------------
        // PASO 5 (COMPENSACIÓN SAGA): Liberar sillas — Ctrl+Z
        // Si el pago falla, devolvemos las sillas al inventario.
        // -------------------------------------------------------
        Console.WriteLine($"[SAGA] Error en pago: {ex.Message}. Liberando sillas...");

        await eventClient.PostAsJsonAsync("/api/events/release",
            new ReservationDto(request.EventId, request.Tickets));

        return Results.Problem(
            detail:     $"El pago falló: {ex.Message} No te preocupes, no te cobramos y tus sillas fueron liberadas.",
            title:      "Pago fallido",
            statusCode: StatusCodes.Status400BadRequest);
    }
})
.WithName("CreateBooking");

app.Run();
