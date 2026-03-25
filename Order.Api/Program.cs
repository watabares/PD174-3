using Order.Api;
using System.Net.Http.Json;
using MassTransit; // <-- NUEVO IMPORT 
using Itm.Shared.Events; // <-- NUEVO IMPORT
using Microsoft.AspNetCore.Identity; // <-- NUEVO IMPORT para Identity (si decides usarlo en el futuro)
using Microsoft.AspNetCore.Diagnostics.HealthChecks; // <-- NUEVO IMPORT para Health Checks
using Microsoft.Extensions.Diagnostics.HealthChecks; // <-- NUEVO IMPORT para Health Checks
using HealthChecks.UI.Client; // <-- NUEVO IMPORT para respuesta JSON estándar de HealthChecks UI
using Microsoft.Extensions.Configuration;


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

// 1.  Registrar el servicio de salud con un check real contra CloudAMQP
builder.Services.AddHealthChecks()
    .AddCheck<CloudAmqpHealthCheck>("CloudAMQP-Broker");

var app = builder.Build(); // Linea divisoria entre configuración y pipeline

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//  Agregamos IPublishEndpoint a los parámetros
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

// 2. Exponer el endpoint de salud en formato JSON estándar para HealthChecks UI
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();

// Health check que verifica conectividad TCP básica contra el broker de CloudAMQP
internal sealed class CloudAmqpHealthCheck : IHealthCheck
{
    private const string AmqpUrl = "amqps://miqffttk:1pscfTN1wGyzJHwe8BTEFMyocp9U-bEp@moose.rmq.cloudamqp.com/miqffttk";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = new Uri(AmqpUrl);
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(uri.Host, uri.Port > 0 ? uri.Port : 5671, cancellationToken);
            return HealthCheckResult.Healthy("CloudAMQP reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CloudAMQP unreachable", ex);
        }
    }
}
