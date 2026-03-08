namespace Itm.Event.Api.Constants;

/// <summary>
/// URLs de los servicios externos consumidos por Event.Api.
/// Se leen primero desde variables de entorno (archivo .env o sistema operativo).
/// Si la variable no existe, se usa el valor predeterminado de localhost.
///
/// Para sobreescribir en desarrollo, crea un archivo .env o define la variable de entorno:
///   EVENT_API_URL=http://localhost:5161
/// </summary>
public static class ServiceUrls
{
    /// <summary>
    /// URL base del propio Event.Api.
    /// Prioridad: variable de entorno EVENT_API_URL → appsettings ServiceUrls:EventApi → localhost:5161.
    /// </summary>
    public static string EventApiBaseUrl(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable("EVENT_API_URL")
        ?? configuration["ServiceUrls:EventApi"]
        ?? "http://localhost:5161";
}
