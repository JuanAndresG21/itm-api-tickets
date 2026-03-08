using Itm.Discount.Api.Constants;
using Itm.Discount.Api.Dtos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Muestra en consola la URL efectiva al arrancar (env var > appsettings > default)
var resolvedUrl = ServiceUrls.DiscountApiBaseUrl(app.Configuration);
app.Logger.LogInformation("[Config] Discount.Api URL: {Url}", resolvedUrl);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// -----------------------------------------------------------
// BASE DE DATOS SIMULADA EN MEMORIA
// -----------------------------------------------------------
var discounts = new List<DiscountModel>
{
    new() { Code = "ITM50", Percentage = 0.5m }
};

// -----------------------------------------------------------
// GET /api/discounts/{code} — Validar código de descuento
// -----------------------------------------------------------
// Retorna el porcentaje si el código existe, 404 si no.
app.MapGet("/api/discounts/{code}", (string code) =>
{
    var discount = discounts.FirstOrDefault(
        d => d.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    if (discount is null)
        return Results.NotFound($"Código de descuento '{code}' no existe.");

    return Results.Ok(new DiscountDto(discount.Code, discount.Percentage));
})
.WithName("GetDiscount");

app.Run();

// -----------------------------------------------------------
// MODELO INTERNO (no se expone directamente al cliente)
// -----------------------------------------------------------
class DiscountModel
{
    public string Code { get; set; } = string.Empty;
    public decimal Percentage { get; set; }
}
