var builder = WebApplication.CreateBuilder(args);

//1. Agregamos YARP a la caja de herramientas (Dependency Injection)
// Le decimos que lea la configuración de rutas desde el appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// 2. Activamos el middleware de YARP para que procese las solicitudes entrantes

app.MapReverseProxy();

app.Run();
