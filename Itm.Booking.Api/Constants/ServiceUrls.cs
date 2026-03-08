namespace Itm.Booking.Api.Constants;

/// <summary>
/// URLs de los servicios externos consumidos por Booking.Api.
/// Prioridad: variable de entorno → appsettings → localhost predeterminado.
///
/// Variables de entorno disponibles:
///   EVENT_API_URL    = http://localhost:5161
///   DISCOUNT_API_URL = http://localhost:5176
/// </summary>
public static class ServiceUrls
{
    public static string EventApiBaseUrl(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable("EVENT_API_URL")
        ?? configuration["ServiceUrls:EventApi"]
        ?? "http://localhost:5161";

    public static string DiscountApiBaseUrl(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable("DISCOUNT_API_URL")
        ?? configuration["ServiceUrls:DiscountApi"]
        ?? "http://localhost:5176";
}
