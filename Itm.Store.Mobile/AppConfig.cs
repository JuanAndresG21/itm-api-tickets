namespace Itm.Store.Mobile;

public static class AppConfig
{
    public static string GatewayBaseUrl =>
    Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://10.0.2.2";
}
