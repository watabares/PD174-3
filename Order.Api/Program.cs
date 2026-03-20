using Order.Api;
using System.Net.Http.Json;
using MassTransit; // <-- NUEVO IMPORT 
using Itm.Shared.Events; // <-- NUEVO IMPORT
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Servicios básicos y Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CONFIGURACIÓN DEL PRODUCTOR (MassTransit)
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        // En un trabajo real, esta URL debe venir de configuración segura (KeyVault / env vars)
        cfg.Host("amqps://miqffttk:1pscfTN1wGyzJHwe8BTEFMyocp9U-bEp@moose.rmq.cloudamqp.com/miqffttk");
    });
});

// Cliente HTTP hacia Inventory.Api (ajusta el puerto si cambia Inventory)
builder.Services.AddHttpClient("InventoryClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5293");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 🧠 Agregamos IPublishEndpoint a los parámetros
app.MapPost(
    "/api/orders",
    async (CreateOrderDto order, IHttpClientFactory factory, IPublishEndpoint publisher) =>
    {
        var invClient = factory.CreateClient("InventoryClient");

        // Paso 1: Intentar reservar el stock
        var reduceResponse = await invClient.PostAsJsonAsync("/api/inventory/reduce", order);

        if (!reduceResponse.IsSuccessStatusCode)
        {
            return Results.BadRequest("No se pudo reservar el stock. Transacción abortada.");
        }

        try
        {
            // Paso 2: Procesar el pago (simulado con un random para este ejemplo)
            bool paymentSuccess = new Random().Next(0, 10) > 5;

            if (!paymentSuccess)
            {
                throw new InvalidOperationException("Fondos Insuficientes en la Tarjeta");
            }

            // Supongamos que la venta fue exitosa y ya cobraron.
            var newOrderId = Guid.NewGuid(); // Simulamos el ID generado
            decimal finalTotal = 150000m;    // Simulamos el total de la venta

            // ---------------------------------------------------------
            // EMISIÓN DEL EVENTO (Patrón Fire and Forget)
            // ---------------------------------------------------------
            // Empacamos la caja
            var orderEvent = new OrderCreatedEvent(newOrderId, order.ProductId, "usuario@correo.itm.edu", finalTotal);

            // La tiramos al buzón de RabbitMQ en la nube
            await publisher.Publish(orderEvent);

            Console.WriteLine($"[BROKER] Evento publicado en CloudAMQP para la orden {newOrderId}");

            return Results.Ok(new { Status = "Orden procesada rápido", OrderId = newOrderId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falló el pago: {ex.Message}. Iniciando compensación de stock...");

            // INCIO DE LA COMPENSACIÓN (SAGA ROLLBACK)
            var compensateResponse = await invClient.PostAsJsonAsync("/api/inventory/release", order);
            if (compensateResponse.IsSuccessStatusCode)
            {
                return Results.Problem("El pago falló. El sttock due devuelto correctamente. Intente de nuevo.");
            }

            Console.WriteLine("[CRITICAL] Falló la compensación. Datos inconsistentes.");
            return Results.Problem("Error crítico del sistema. Contacte soporte.");
        }
    });

app.Run();