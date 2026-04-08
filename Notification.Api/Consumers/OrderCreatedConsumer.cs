using MassTransit; // <-- NUEVO IMPORT
using Itm.Shared.Events; // <-- NUEVO IMPORT
using Microsoft.Extensions.Logging;

namespace Itm.Notification.Api.Consumers;

// Heredar de IConsumer le dice al motor : "Despiértame cuando llegue este tipo de evento" 
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        // MassTransit propaga automáticamente el CorrelationId en el contexto del mensaje
        var correlationId = context.CorrelationId?.ToString() ?? "SIN-ID";

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        var data = context.Message; // Abrimos la "caja" con los datos del evento

        _logger.LogInformation("Procesando recibo de compra para la orden {OrderId} y usuario {UserEmail}", data.OrderId, data.UserEmail);
        // Simulamos la lentitud de un servidor de correos real (4 segundos)
        await Task.Delay(4000);
        _logger.LogInformation("Correo enviado exitosamente a {UserEmail}", data.UserEmail);
    }
}
