using Order.Api;
using System.Net.Http.Json;
using MassTransit;
using Itm.Shared.Events;
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

// Endpoint de creación de orden con lógica SAGA (reservar stock + compensar si falla pago)
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
        // Paso2: Procesar el pago (simulado con un random para este ejemplo)
        bool paymentSuccess = new Random().Next(0, 10) > 5;

        if (!paymentSuccess)
        {
            throw new InvalidOperationException("Fondos Insuficientes en la Tarjeta");
        }
        // Supongamos que la venta fue exitosa y ya cobraron.

        var newOrderId = Guid.NewGuid();
        decimal simlatedTotal = 150000m; // En real viene de Price.Api

        // Ajustar a la firma real de OrderCreatedEvent (sin UserEmail)
        var eventMessage = new OrderCreatedEvent(
            OrderId: newOrderId,
            ProductId: order.ProductId,
            UserEmail: "estudiante@correo.itm.edu.co", //simulado, en real viene del token de autenticación
            TotalAmount: simlatedTotal
        );

        await publisher.Publish(eventMessage);
        Console.WriteLine($"[SISTEMA] Evento OrderCreated publicado para la orden {newOrderId}");
        return Results.Ok(new
        {
            Message = "Orden creada y Pagada exitosamente",
            OrderId = newOrderId
        });
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



