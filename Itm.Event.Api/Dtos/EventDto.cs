namespace Itm.Event.Api.Dtos;

/// <summary>Contrato de respuesta para un evento.</summary>
public record EventDto(int Id, string Name, decimal BasePrice, int AvailableSeats);

/// <summary>Contrato de entrada para reservar o liberar sillas.</summary>
public record ReservationDto(int EventId, int Quantity);
