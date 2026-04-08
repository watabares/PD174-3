using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// 1. REGISTRO DE SERVICIOS (Inyección de Dependencias)
// ---------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// ARQUITECTURA: Registramos los HttpClientFactory
// Cliente 1: Inventario
builder.Services.AddHttpClient("InventoryClient", client =>
{
    //  Ajustar al puerto de la API de Inventario
    client.BaseAddress = new Uri("http://localhost:5293");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Cliente 2: Precios
builder.Services.AddHttpClient("PriceClient", client =>
{
    //  Ajustado al puerto real de la API de Precios
    client.BaseAddress = new Uri("http://localhost:5012");
});

var app = builder.Build();

// ---------------------------------------------------------
// 2. PIPELINE HTTP
// ---------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ---------------------------------------------------------
// 3. ENDPOINTS
// ---------------------------------------------------------

// Endpoint Anterior: Consulta simple (Solo inventario)
app.MapGet("/api/products/{id}/check-stock", async (int id, IHttpClientFactory clientFactory, HttpContext httpContext) =>
{
 // Propagamos el Authorization header recibido (JWT) hacia Inventory.Api
    if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        // Guardamos el token en el HttpContext.Items para que pueda ser reutilizado si en el futuro
        // agregamos un DelegatingHandler. Por ahora lo usamos directamente en el cliente.
        // (Decisión simple para este laboratorio.)
    }

    var client = clientFactory.CreateClient("InventoryClient");
    try
    {
       // Copiamos el Authorization header si existe, para que Inventory valide el mismo JWT
        if (httpContext.Request.Headers.TryGetValue("Authorization", out var auth))
        {
            client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.Add("Authorization", (IEnumerable<string>)auth);
        }

        var response = await client.GetAsync($"/api/inventory/{id}");
        if (response.IsSuccessStatusCode)
        {
            var inventoryData = await response.Content.ReadFromJsonAsync<InventoryResponse>();
            return Results.Ok(new { ProductId = id, StockInfo = inventoryData });
        }
        return Results.Problem($"El inventario respondió con error: {response.StatusCode}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error de conexión: {ex.Message}");
    }
})
.WithName("CheckProductStock")
.WithOpenApi();


//  PASO 3: EL CORAZÓN DE LA CLASE - Implementación de Paralelismo
// Endpoint: El BFF consolida la información de múltiples microservicios en paralelo.
app.MapGet("/api/products/{id}/summary", async (int id, IHttpClientFactory factory) =>
{
    // 1. Obtenemos las herramientas (Clientes)
    var invClient = factory.CreateClient("InventoryClient");
    var priceClient = factory.CreateClient("PriceClient");

    try
    {
        // 2. INICIO DEL PARALELISMO
        // Lanzamos la petición, PERO NO ponemos 'await' todavía.
        // La tarea queda "volando" (Running) en segundo plano por la red.
        var inventoryTask = invClient.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{id}");
        var priceTask = priceClient.GetFromJsonAsync<PriceResponse>($"/api/prices/{id}");

        // 3. PUNTO DE SINCRONIZACIÓN
        // Aquí el hilo se detiene a esperar que AMBAS terminen.
        // El tiempo de espera será igual al del microservicio más lento, no la suma de ambos.
        await Task.WhenAll(inventoryTask, priceTask);

        // 4. Extracción de resultados (Ya están listos en memoria)
        var inventoryData = inventoryTask.Result;
        var priceData = priceTask.Result;

        // 5. Composición (BFF pattern - JSON Agregado)
        return Results.Ok(new
        {
            Id = id,
            Product = "Laptop Gamer Pro",
            // Unimos los datos de dos mundos diferentes:
            StockDetails = inventoryData,
            FinancialDetails = priceData,
            CalculatedAt = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        //  Análisis de Fallo:
        // Si UNO de los dos falla, Task.WhenAll lanza excepción.
        // En clases futuras veremos cómo manejar fallos parciales (Circuit Breaker avanzado).
        return Results.Problem($"Error en el ecosistema distribuido: {ex.Message}");
    }
})
.WithName("GetProductSummary")
.WithOpenApi();

// Endpoint de salud en formato JSON compatible con HealthChecks UI
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run(); // FIN DE LAS INSTRUCCIONES DE NIVEL SUPERIOR

// ---------------------------------------------------------
// 4. MODELOS LOCALES (DTOS) - SIEMPRE VAN AL FINAL
// ---------------------------------------------------------
// DTOs Locales para mapear las respuestas de los otros microservicios
internal record InventoryResponse(int ProductId, int Stock, string Sku);
internal record PriceResponse(int ProductId, decimal Amount, string Currency);