# ITM Distributed System - Microservicios de Ejemplo

Sistema distribuido de ejemplo para el curso de Arquitectura de Software (Clases 1 a 7).

Este proyecto muestra cómo pasar de un monolito a una arquitectura de microservicios usando .NET 8, Minimal APIs y un cliente móvil .NET MAUI (.NET 9):

- `Itm.Inventory.Api` – Servicio de Inventario (dueño del stock, protegido con JWT).
- `Itm.Price.Api` – Servicio de Precios (dueño del dinero).
- `Itm.Product.Api` – Orquestador / BFF que compone información de Inventario y Precios.
- `Order.Api` – Servicio de Órdenes que primero actúa como orquestador clásico (Clase 3) y luego implementa una SAGA orquestada (Clase 4) y publica eventos de dominio.
- `Itm.Gateway.Api` – API Gateway basado en YARP que expone solo las rutas necesarias hacia los microservicios internos y centraliza la seguridad de entrada.
- `Notification.Api` – Servicio de notificaciones que actúa como consumidor de eventos de dominio (por ejemplo, `OrderCreatedEvent`) publicados por `Order.Api` a través de RabbitMQ/MassTransit.
- `Itm.Store.Mobile` – App móvil .NET MAUI (.NET 9) que consume el ecosistema a través del Gateway, usando JWT almacenado en `SecureStorage` y un `AuthHandler` para enviar llamadas seguras.

---

## 1. Requisitos

- Visual Studio 2022 o superior (carga de trabajo "Desarrollo ASP.NET y Web").
- SDK .NET 8.0 instalado.

Verificar SDK:

```bash
dotnet --version
```

---

## 2. Proyectos de la solución

### 2.1. Itm.Inventory.Api

Microservicio que expone y muta el stock disponible de cada producto.

- DTOs principales:
  - `InventoryDto` – respuesta de stock (`productId`, `stock`, `sku`).
  - `ReduceStockDto` – input para reducir stock (`productId`, `quantity`).
- Endpoints principales:
  - `GET /api/inventory/{id}` – devuelve inventario de un producto (protegido con JWT).
  - `POST /api/inventory/reduce` – reduce stock; valida existencia y cantidad.
  - `POST /api/inventory/release` – compensación: devuelve stock ante fallo de pago (SAGA).

Ejemplo de `POST /api/inventory/reduce` (Clase 3):

```csharp
app.MapPost("/api/inventory/reduce", (ReduceStockDto request) =>
{
    var item = inventoryDb.FirstOrDefault(p => p.ProductId == request.ProductId);
    if (item is null)
    {
        return Results.NotFound(new { Error = "Producto no existe en bodega" });
    }

    if (item.Stock < request.Quantity)
    {
        return Results.BadRequest(new { Error = "Stock insuficiente", CurrentStock = item.Stock });
    }

    var index = inventoryDb.IndexOf(item);
    inventoryDb[index] = item with { Stock = item.Stock - request.Quantity };

    return Results.Ok(new { Message = "Stock actualizado", NewStock = inventoryDb[index].Stock });
});
```

Ejemplo de `POST /api/inventory/release` (Clase 4):

```csharp
app.MapPost("/api/inventory/release", (ReduceStockDto request) =>
{
    var item = inventoryDb.FirstOrDefault(p => p.ProductId == request.ProductId);
    if (item is null) return Results.NotFound();

    var index = inventoryDb.IndexOf(item);
    inventoryDb[index] = item with { Stock = item.Stock + request.Quantity };

    return Results.Ok(new { Message = "Stock liberado por fallo en transacción", CurrentStock = inventoryDb[index].Stock });
});
```

Seguridad (JWT) configurada vía `JwtSettings` en `appsettings.json` y `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` en `Program.cs`.

### 2.2. Itm.Price.Api

Microservicio de precios. Devuelve el valor y la moneda de un producto.

- Endpoint: `GET /api/prices/{id}`.
- Usado por `Itm.Product.Api` y `Order.Api` para calcular montos.

### 2.3. Itm.Product.Api

Backend for Frontend (BFF). Orquesta llamados a Inventario y Precios en paralelo usando `Task.WhenAll`.

Endpoints clave:

- `GET /api/products/{id}/check-stock` – solo inventario.
- `GET /api/products/{id}/summary` – inventario + precio en paralelo.

### 2.4. Order.Api (Clases 3 y 4)

Microservicio de órdenes que cumple dos roles a lo largo del curso:

1. **Clase 3 – Orquestador clásico:**
   - Recibe una orden (`productId`, `quantity`).
   - Llama a `Inventory.Api` para verificar stock (GET o POST reduce).
   - Llama a `Price.Api` para obtener precio.
   - Calcula total y devuelve una "factura" JSON.

2. **Clase 4 – SAGA orquestada:**
   - Reserva stock en `Itm.Inventory.Api` (`/reduce`).
   - Simula pago.
   - Si el pago falla, compensa llamando a `/release`.

Ejemplo simplificado de la SAGA (actual `Program.cs`):

```csharp
app.MapPost("/api/orders", async (CreateOrderDto order, IHttpClientFactory factory) =>
{
    var invClient = factory.CreateClient("InventoryClient");

    var reduceResponse = await invClient.PostAsJsonAsync("/api/inventory/reduce", order);
    if (!reduceResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest("No se pudo reservar el stock. Transacción abortada.");
    }

    try
    {
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

        var compensateResponse = await invClient.PostAsJsonAsync("/api/inventory/release", order);

        if (compensateResponse.IsSuccessStatusCode)
        {
            return Results.Problem("El pago falló. El stock fue devuelto correctamente. Intente de nuevo.");
        }

        Console.WriteLine("[CRITICAL] Falló la compensación. Datos inconsistentes.");
        return Results.Problem("Error crítico del sistema. Contacte soporte.");
    }
});
```

### 2.5. Itm.Gateway.Api (Clase 5 – API Gateway con YARP)

Gateway basado en [YARP](https://microsoft.github.io/reverse-proxy/) que actúa como **punto de entrada único** para los microservicios. En este ejemplo, enruta hacia `Itm.Inventory.Api`.

Configuración de `ReverseProxy` y `Program.cs` descrita en la versión anterior del README.

### 2.6. Clase 6 - Event-Driven Architecture y Mensajería Asíncrona

En la Clase 6 se introduce un **Message Broker (RabbitMQ)** usando **CloudAMQP** (PaaS) para evitar instalaciones locales y habilitar una arquitectura **event-driven**:

- Se provisiona un clúster gratuito en CloudAMQP y se usa la **AMQP URL** en los servicios.
- Se crea la librería compartida `Itm.Shared.Events` con el evento inmutable `OrderCreatedEvent`.
- `Order.Api` se convierte en **productor** usando **MassTransit.RabbitMQ**, publica `OrderCreatedEvent` a RabbitMQ tras completar la SAGA.
- Se agrega `Notification.Api` como **consumidor**, con un `OrderCreatedConsumer` que procesa `OrderCreatedEvent` y simula el envío de correos.
- Se demuestra desacoplamiento temporal: las órdenes se completan aunque `Notification.Api` esté caído, los mensajes quedan retenidos en la cola y se procesan al reactivar el servicio.

Esta clase consolida el paso de integración síncrona (HTTP) a mensajería asíncrona, mejorando resiliencia, escalabilidad y experiencia de usuario.

### 2.7. Clase 7 - Cliente móvil seguro con .NET MAUI y JWT

Se añade el cliente móvil:

- Se crea `Itm.Store.Mobile` como app móvil .NET MAUI (.NET 9).
- Se configura un `HttpClient` llamado `GatewayClient` apuntando a `Itm.Gateway.Api` (10.0.2.2:5110 en emulador Android).
- Se implementa un `AuthHandler` que lee un JWT desde `SecureStorage` y lo envía como `Authorization: Bearer ...` en cada petición.
- El flujo completo es: **Móvil → Gateway → Product.Api → Inventory.Api**.
  - Sin iniciar sesión: la llamada al Gateway termina en `401 Unauthorized` al llegar a `Inventory.Api`.
  - Tras "Iniciar Sesión": se guarda un JWT válido y la misma ruta retorna `200 OK` con el JSON del stock.

---

## 3. Clases 1–7 resumidas

### Clase 1–2: Fundamentos, BFF y paralelismo

- Separación de dominios: `Inventory`, `Price`, `Product`.
- Uso de DTOs (`record`) para bajo acoplamiento.
- `HttpClientFactory` para llamadas entre servicios.
- `Task.WhenAll` para reducir latencia en consultas compuestas.

### Clase 3: Mutación de estado e integración inicial (Order.Api)

- Se introduce `POST` en `Itm.Inventory.Api` para **mutar estado** (`/reduce`).
- `Order.Api` orquesta stock y precio para generar una orden/factura.

### Clase 4: Transacciones distribuidas y SAGA

- Problema: pago fallido con stock ya descontado.
- Solución: **Patrón SAGA Orquestada** con acciones compensatorias (`reduce` + `release`).

### Clase 5: API Gateway y seguridad con JWT

- Se introduce `Itm.Gateway.Api` como API Gateway con YARP.
- Se protege `Itm.Inventory.Api` con JWT.
- Defensa en profundidad: Gateway + microservicios seguros.

### Clase 6: Arquitectura Orientada a Eventos (EDA) y Mensajería Asíncrona

- Se introduce un **Message Broker (RabbitMQ)** usando **CloudAMQP**.
- Se crea la librería compartida `Itm.Shared.Events` con el evento inmutable `OrderCreatedEvent`.
- `Order.Api` publica `OrderCreatedEvent` a RabbitMQ mediante MassTransit.
- `Notification.Api` consume `OrderCreatedEvent` y simula el envío de correos.

### Clase 7: Cliente móvil seguro con .NET MAUI y JWT

- Se crea `Itm.Store.Mobile` como app móvil .NET MAUI (.NET 9).
- Se configura un `HttpClient` llamado `GatewayClient` apuntando a `Itm.Gateway.Api` (10.0.2.2:5110 en emulador Android).
- Se implementa un `AuthHandler` que lee un JWT desde `SecureStorage` y lo envía como `Authorization: Bearer ...` en cada petición.
- El flujo completo es: **Móvil → Gateway → Product.Api → Inventory.Api**.
  - Sin iniciar sesión: la llamada al Gateway termina en `401 Unauthorized` al llegar a `Inventory.Api`.
  - Tras "Iniciar Sesión": se guarda un JWT válido y la misma ruta retorna `200 OK` con el JSON del stock.

---

## 4. Cómo ejecutar escenarios

(Se mantienen las secciones de ejecución anteriores, añadiendo que en Clase 3 el foco es `Inventory + Order + Price` para la factura inicial, y en Clase 4 se activa SAGA con compensación.)

---

## 5. Notas de arquitectura

El sistema demuestra:

- **Desacoplamiento** entre dominios y contratos claros vía DTOs.
- **Orquestación sincronizada** (BFF y `Order.Api`).
- **Mutación de estado controlada** con validaciones de negocio.
- **Consistencia eventual** mediante SAGA con acciones compensatorias.
- **Defensa en profundidad** con API Gateway (YARP) y JWT.
- **Arquitectura orientada a eventos** con RabbitMQ, MassTransit y colas de mensajes.
- **Cliente móvil seguro** con .NET MAUI, JWT y `SecureStorage` para integrar front móvil con backend distribuido.

En un entorno productivo se añadirían colas de mensajes para hacer SAGA asíncrona y mejorar resiliencia frente a fallos intermedios.