using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

namespace Itm.Store.Mobile;

public partial class MainPage : ContentPage
{
    private readonly IHttpClientFactory _httpClientFactory;
    private HubConnection? _hubConnection;

    private record TokenResponse(string Token, string ExpiresIn);
    private record TicketReadyDto(Guid BookingId, int EventId, int Tickets, decimal TotalAmount);

    // Inyectamos la fábrica, tal como lo hacemos en el Backend
    public MainPage(IHttpClientFactory httpClientFactory)
    {
        InitializeComponent();
        _httpClientFactory = httpClientFactory;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        try
        {
            ResultLabel.Text = "Solicitando JWT...";
            ResultLabel.TextColor = Colors.Orange;

            var client = _httpClientFactory.CreateClient("GatewayClient");
            var response = await client.PostAsJsonAsync("/api/auth/token", new
            {
                username = "itm",
                password = "2026"
            });

            if (!response.IsSuccessStatusCode)
            {
                ResultLabel.Text = await FormatResponseDetails(response, "LOGIN");
                ResultLabel.TextColor = Colors.Red;
                return;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.Token))
            {
                ResultLabel.Text = "No se pudo leer el token.";
                ResultLabel.TextColor = Colors.Red;
                return;
            }

            await SecureStorage.Default.SetAsync("jwt_token", tokenResponse.Token);
            await EnsureHubConnectionAsync();

            ResultLabel.Text = "Token JWT guardado y SignalR conectado.";
            ResultLabel.TextColor = Colors.Green;
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"ERROR DE RED:\n{ex}";
            ResultLabel.TextColor = Colors.Red;
        }
    }

    private async void OnGetDataClicked(object sender, EventArgs e)
    {
        try
        {
            ResultLabel.Text = "Creando reserva...";
            ResultLabel.TextColor = Colors.Orange;

            var client = _httpClientFactory.CreateClient("GatewayClient");

            var response = await client.PostAsJsonAsync("/api/bookings/secure", new
            {
                eventId = 1,
                tickets = 1,
                discountCode = "ITM50"
            });

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsStringAsync();
                ResultLabel.Text = $"ÉXITO:\n{data}";
                ResultLabel.TextColor = Colors.Green;
            }
            else
            {
                ResultLabel.Text = await FormatResponseDetails(response, "BOOKING");
                ResultLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"ERROR DE RED:\n{ex}";
            ResultLabel.TextColor = Colors.Red;
        }
    }

    private static async Task<string> FormatResponseDetails(HttpResponseMessage response, string context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{context} ERROR {(int)response.StatusCode} {response.ReasonPhrase}");

        if (response.RequestMessage?.RequestUri is not null)
        {
            builder.AppendLine($"URL: {response.RequestMessage.RequestUri}");
        }

        builder.AppendLine($"BaseUrl: {AppConfig.GatewayBaseUrl}");

        if (response.Headers is not null)
        {
            builder.AppendLine("Headers:");
            foreach (var header in response.Headers)
            {
                builder.AppendLine($"- {header.Key}: {string.Join(", ", header.Value)}");
            }
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                builder.AppendLine($"- {header.Key}: {string.Join(", ", header.Value)}");
            }

            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                builder.AppendLine("Body:");
                builder.AppendLine(body);
            }
        }

        return builder.ToString();
    }

    private async Task EnsureHubConnectionAsync()
    {
        if (_hubConnection is not null && _hubConnection.State != HubConnectionState.Disconnected)
            return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri($"{AppConfig.GatewayBaseUrl}/hubs/tickets"), options =>
            {
                options.AccessTokenProvider = () => SecureStorage.Default.GetAsync("jwt_token");
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<TicketReadyDto>("ticket-ready", payload =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ResultLabel.Text = $"Boleta lista: {payload.BookingId}\nTotal: {payload.TotalAmount}"
                                 + $"\nEvento: {payload.EventId} | Tickets: {payload.Tickets}";
                ResultLabel.TextColor = Colors.Blue;
            });
        });

        await _hubConnection.StartAsync();
    }
}