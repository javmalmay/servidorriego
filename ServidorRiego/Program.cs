using Serilog;
using Serilog.Events;
using ServidorRiego.Data;
using ServidorRiego.Realtime;
using ServidorRiego.Services;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

// Logging: Serilog reemplaza los providers por defecto y escribe tanto a consola (se sigue viendo
// con journalctl si se ejecuta bajo systemd, ya que systemd captura stdout) como a un fichero con
// rotación diaria y retención de 14 días. La ruta es configurable con "Logging:FilePath";
// appsettings.Production.json la fija en /var/log/servidor-riego para el despliegue en Ubuntu. En
// desarrollo (o si no se configura) cae en una carpeta relativa "logs/", que también funciona en Windows.
var logFilePath = builder.Configuration["Logging:FilePath"] ?? "logs/servidor-riego-.log";
builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            logFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
});

// Add services to the container.
builder.Services.AddControllers();

// Configurar Entity Framework con SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=riego.db";
builder.Services.AddDbContext<RiegoDbContext>(options =>
    options.UseSqlite(connectionString));

// Configurar JWT
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException("JWT Key must be configured in appsettings.json");
}

var key = Encoding.ASCII.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    // El endpoint WebSocket /ws/user no puede fijar la cabecera Authorization desde
    // muchos clientes (navegadores, librerías WS sencillas), así que también se acepta
    // el JWT como query string "access_token", convención habitual para WebSockets/SignalR.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/ws/user"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// Añadir servicios de aplicación
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddSingleton<WebSocketConnectionManager>();

// Rate limiting para endpoints públicos sensibles a abuso (p. ej. registro de usuarios).
// En Development se desactiva (sin límite) para no interferir con las pruebas desde Swagger.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("register", httpContext =>
    {
        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (env.IsDevelopment())
        {
            return RateLimitPartition.GetNoLimiter("development");
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        });
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Agregar Swagger/Swashbuckle
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Servidor de Riego API",
        Version = "v1",
        Description = "API REST para controlar dispositivos de riego desde aplicación Android",
        Contact = new()
        {
            Name = "Servidor de Riego",
            Url = new Uri("https://github.com")
        },
        License = new()
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });

    // Agregar soporte para autenticación JWT en Swagger
    options.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header usando Bearer scheme.\r\n\r\nPega SOLO el token (sin la palabra \"Bearer\"), Swagger la añade automáticamente. Ejemplo: eyJhbGciOiJIUzI1NiIs..."
    });

    options.AddSecurityRequirement(new()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });

    // Incluir comentarios XML en la documentación
    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

// Añadir CORS si es necesario para la aplicación Android
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAndroid", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Aplicar migraciones de base de datos automáticamente
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RiegoDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Servidor de Riego API v1");
        options.RoutePrefix = string.Empty; // Swagger en la raíz
        options.DocExpansion(DocExpansion.List);
        options.DefaultModelsExpandDepth(2);
    });
    app.UseDeveloperExceptionPage();
}

// En Development NO forzamos redirección a HTTPS: Swagger sirve la UI en
// http://localhost:5096 y, si se redirige a https://localhost:7179 (otro origen),
// el navegador descarta la cabecera Authorization al seguir el 307, provocando
// 401 aunque el token sea válido. En producción sí queremos forzar HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAndroid");

// Debe ir antes de Authentication/Authorization: habilita el protocolo de upgrade
// a WebSocket que usan /ws/device y /ws/user.
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapRiegoWebSockets();

app.Run();
