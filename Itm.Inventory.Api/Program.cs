using Itm.Inventory.Api.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using HealthChecks.UI.Client;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Servicios básicos (Swagger)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Bloque de seguridad JWT
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],

            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secretKey)
        };
    });

builder.Services.AddAuthorization();

//1. Registar el servicio de salud
// Estamos enseñando a nuestra aplicacion a tomarse el pulso a si misma, para que herramientas externas puedan saber si esta funcionando correctamente o no.
builder.Services.AddHealthChecks();

var app = builder.Build();

// 3. Pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Primero autenticación, luego autorización
app.UseAuthentication();
app.UseAuthorization();

// 4. "Base de datos" en memoria
var inventoryDb = new List<InventoryItemDto>
{
    new(1, 50, "LAPTOP-DELL"),
    new(2, 0, "MOUSE-LOGI"),
    new(3, 100, "TECLADO-RGB")
};

// 5. Endpoint protegido
app.MapGet("/api/inventory/{id}", (int id) =>
{
    var item = inventoryDb.FirstOrDefault(p => p.ProductId == id);
    return item is not null ? Results.Ok(item) : Results.NotFound();
})
.WithName("GetInventory")
.WithOpenApi()
.RequireAuthorization();

// 2. Exponer el endpoint de salud en formato JSON compatible con HealthChecks UI
// Abrimos la "Puerta" en el servidor para que el Gateway (o nosotros) pueda preguntar por la salud.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});


app.Run();