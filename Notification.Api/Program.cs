using MassTransit; // <-- NUEVO IMPORT
using Itm.Notification.Api.Consumers; // <-- NUEVO IMPORT
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
    // Registramos nuestro obrero de eventos (consumidor) para que MassTransit sepa a qué eventos debe "despertar" esta aplicación
    x.AddConsumer<OrderCreatedConsumer>(); // <-- REGISTRAMOS EL CONSUMIDOR
    x.UsingRabbitMq((context, cfg) =>
    {
        // En un trabajo real, esta URL debe venir de configuración segura (KeyVault / env vars)
        cfg.Host("amqps://miqffttk:1pscfTN1wGyzJHwe8BTEFMyocp9U-bEp@moose.rmq.cloudamqp.com/miqffttk");

        // Configuramos el nombre de la "fila" donde el obrero va a escuchar
        cfg.ReceiveEndpoint("notificaciones-cola", e =>
        {
            // Le decimos a esta fila que cuando llegue un evento del tipo OrderCreatedEvent, despierte al OrderCreatedConsumer
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });
    });
});

// Registro básico de health checks para que el Gateway pueda monitorear este servicio
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => "Notification.Api esperando mensajes de la nube...");

// Endpoint de salud en formato JSON compatible con HealthChecks UI
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
