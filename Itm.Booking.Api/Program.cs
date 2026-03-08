using Itm.Booking.Api.Constants;
using Itm.Booking.Api.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Net.Http.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// -----------------------------------------------------------
// SWAGGER CON SOPORTE JWT
// -----------------------------------------------------------
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Ingresa tu token JWT. Ejemplo: eyJhbGci..."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// -----------------------------------------------------------
// AUTENTICACIÓN JWT
// -----------------------------------------------------------
var jwtKey    = builder.Configuration["Jwt:Key"]    ?? "itm-secret-key-super-segura-2026!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Itm.Booking.Api";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = false,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

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

// Cliente interno para que /api/bookings/secure delegue a /api/bookings
builder.Services
    .AddHttpClient("BookingInternal", client =>
    {
        client.BaseAddress = new Uri(
            Environment.GetEnvironmentVariable("BOOKING_API_URL")
            ?? config["ServiceUrls:BookingApi"]
            ?? "http://localhost:5148");
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// -----------------------------------------------------------
// POST /api/auth/token — Genera un JWT de prueba
// -----------------------------------------------------------
// En producción esto lo haría un servicio de identidad real (Keycloak, Auth0, etc.).
// Aquí lo exponemos solo para hacer pruebas sin herramienta externa.
app.MapPost("/api/auth/token", (LoginRequestDto login) =>
{
    // Credenciales de prueba fijas (solo para demo)
    if (login.Username != "itm" || login.Password != "2026")
        return Results.Unauthorized();

    var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds   = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token   = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
        issuer:             jwtIssuer,
        claims:             null,
        expires:            DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { Token = jwt, ExpiresIn = "1h" });
})
.WithName("GetToken")
.WithSummary("Generar Token JWT")
.WithDescription("Credenciales de prueba: user=itm | pass=2026")
.AllowAnonymous();

// -----------------------------------------------------------
// POST /api/bookings/secure — Igual que /api/bookings pero requiere JWT
// -----------------------------------------------------------
app.MapPost("/api/bookings/secure", async (BookingRequestDto request, IHttpClientFactory factory) =>
{
    // Reutiliza exactamente la misma lógica del endpoint público
    // delegando la petición internamente al propio servicio.
    var bookingClient = factory.CreateClient("BookingInternal");
    var response = await bookingClient.PostAsJsonAsync("/api/bookings", request);
    var content  = await response.Content.ReadAsStringAsync();

    return Results.Content(content,
        contentType: "application/json",
        statusCode:  (int)response.StatusCode);
})
.WithName("CreateSecureBooking")
.RequireAuthorization();

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
