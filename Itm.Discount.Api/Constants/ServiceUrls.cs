namespace Itm.Discount.Api.Constants;

/// <summary>
/// URLs de los servicios externos consumidos por Discount.Api.
/// Prioridad: variable de entorno → appsettings → localhost predeterminado.
///
/// Para sobreescribir en desarrollo, define la variable de entorno:
///   DISCOUNT_API_URL=http://localhost:5176
/// </summary>
public static class ServiceUrls
{
    /// <summary>
    /// URL base del propio Discount.Api.
    /// Prioridad: variable de entorno DISCOUNT_API_URL → appsettings ServiceUrls:DiscountApi → localhost:5176.
    /// </summary>
    public static string DiscountApiBaseUrl(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable("DISCOUNT_API_URL")
        ?? configuration["ServiceUrls:DiscountApi"]
        ?? "http://localhost:5176";
}
