namespace Itm.Booking.Api.Dtos;

/// <summary>Petición del cliente para reservar entradas.</summary>
public record BookingRequestDto(int EventId, int Tickets, string DiscountCode);

/// <summary>Contrato recibido desde Event.Api.</summary>
public record EventDto(int Id, string Name, decimal BasePrice, int AvailableSeats);

/// <summary>Contrato recibido desde Discount.Api.</summary>
public record DiscountDto(string Code, decimal Percentage);

/// <summary>DTO interno para enviar a Event.Api (reserve / release).</summary>
public record ReservationDto(int EventId, int Quantity);

/// <summary>Factura de confirmación retornada al cliente tras un pago exitoso.</summary>
public record BookingInvoiceDto(
    string  Status,
    string  EventName,
    int     Tickets,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    string  Message);
