using MassTransit; // <-- NUEVO IMPORT
using Itm.Shared.Events; // <-- NUEVO IMPORT


namespace Itm.Notification.Api.Consumers;

// Heredar de IConsumer le dice al motor : "Despiertame cuando llegue este tipo de evento" 
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>

{
    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var data = context.Message; // <-- Aquí tienes acceso a los datos del evento - Abrimo la caja

        Console.WriteLine("\n =====================================");
        Console.WriteLine($"[Enviando recibo de compra]");
        Console.WriteLine($"Para: {data.UserEmail}");
        Console.WriteLine("$Orden: {data.OrderId}");
        Console.WriteLine("$Monto a Cobrar: ${data.TotalAmount}");
        Console.WriteLine("=====================================\n");

        // Simulamos la lentitud de un servidor de correos real (4 segundos)
        await Task.Delay(4000);

        Console.WriteLine($" Correo enviado exitosamente a {data.UserEmail}");
    }
}
