using Order.Api;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Servicios básicos y Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
app.MapPost("/api/orders", async (CreateOrderDto order, IHttpClientFactory factory) =>
{
    var invClient = factory.CreateClient("InventoryClient");

    // PASO 1: Intentar reservar Stock (La Acción)
    var reduceResponse = await invClient.PostAsJsonAsync("/api/inventory/reduce", order);

    if (!reduceResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest("No se pudo reservar el stock. Transacción abortada.");
    }

    try
    {
        // PASO 2: Procesar el Pago (Simulación de Fallo)
        bool paymentSuccess = new Random().Next(0, 10) > 5;

        if (!paymentSuccess)
        {
            throw new InvalidOperationException("Fondos Insuficientes en la Tarjeta");
        }

        return Results.Ok(new { Message = "Orden Creada y Pagada Exitosamente" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Falló el pago: {ex.Message}. Iniciando compensación...");

        // INICIO DE LA COMPENSACIÓN (SAGA ROLLBACK)
        var compensateResponse = await invClient.PostAsJsonAsync("/api/inventory/release", order);

        if (compensateResponse.IsSuccessStatusCode)
        {
            return Results.Problem("El pago falló. El stock fue devuelto correctamente. Intente de nuevo.");
        }
        else
        {
            Console.WriteLine("[CRITICAL] Falló la compensación. Datos inconsistentes.");
            return Results.Problem("Error crítico del sistema. Contacte soporte.");
        }
    }
});

app.Run();