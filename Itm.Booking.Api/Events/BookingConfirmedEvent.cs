namespace Itm.Booking.Api.Events;

public record BookingConfirmedEvent(
    Guid BookingId,
    int EventId,
    int Tickets,
    decimal TotalAmount);
