using Itm.Booking.Api.Events;
using Itm.Booking.Api.Hubs;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace Itm.Booking.Api.Consumers;

public class BookingConfirmedConsumer : IConsumer<BookingConfirmedEvent>
{
    private readonly IHubContext<TicketHub> _hubContext;

    public BookingConfirmedConsumer(IHubContext<TicketHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<BookingConfirmedEvent> context)
    {
        var evt = context.Message;

        // Simulate async ticket generation
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        await _hubContext.Clients.All.SendAsync("ticket-ready", new
        {
            evt.BookingId,
            evt.EventId,
            evt.Tickets,
            evt.TotalAmount
        });
    }
}
