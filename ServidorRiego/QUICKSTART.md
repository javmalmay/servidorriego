# ?? Servidor de Riego - Guía de Inicio Rápido

## ¿Qué se ha creado?

Se ha implementado un servicio backend completo en .NET 9 con:

### ? Base de Datos SQLite
- Tabla `Users` con campos: Id, Username, Email, PasswordHash, CreatedAt, LastLogin, IsActive
- Indices únicos en Username y Email
- Migraciones automáticas aplicadas al iniciar

### ? Autenticación JWT
- Generación de tokens JWT seguros
- Validación de tokens en endpoints protegidos
- Tokens con expiración (60 minutos por defecto)
- Contraseñas hasheadas con BCrypt

### ? Endpoints REST
- **POST /api/auth/register** - Registro de nuevos usuarios
- **POST /api/auth/login** - Inicio de sesión y generación de token
- **GET /api/auth/profile** - Obtener perfil del usuario (protegido)

### ? CORS Habilitado
- Configurado para aceptar solicitudes desde cualquier origen
- Ideal para conexiones desde aplicaciones Android

## ?? Pasos para Ejecutar

### 1. Configurar la clave JWT (IMPORTANTE)
Edita `appsettings.json` y cambia la clave JWT por una más segura:

```bash
# En PowerShell, genera una clave segura:
[Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((New-Guid).Guid + (New-Guid).Guid + (New-Guid).Guid))
```

Luego copia el resultado en `appsettings.json`:
```json
"Jwt": {
  "Key": "AQUI_TU_CLAVE_GENERADA",
  ...
}
```

### 2. Ejecutar el servidor
```bash
cd ServidorRiego
dotnet run
```

El servidor se iniciará en `https://localhost:5001`

### 3. Probar los endpoints

#### Opción A: Usando curl
```bash
# Registro
curl -X POST https://localhost:5001/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"usuario1","email":"user@example.com","password":"Pass123!"}'

# Login
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"usuario1","password":"Pass123!"}'

# Obtener perfil (reemplaza TOKEN con el token recibido)
curl -X GET https://localhost:5001/api/auth/profile \
  -H "Authorization: Bearer TOKEN"
```

#### Opción B: Usando REST Client (VS Code)
1. Instala la extensión "REST Client"
2. Abre `test_endpoints.http`
3. Haz clic en "Send Request" en cada endpoint

#### Opción C: Usando Postman
1. Crea una nueva colección
2. Importa los endpoints del archivo `test_endpoints.http`
3. Ejecuta cada solicitud

## ?? Integración con Android

Para conectar desde tu aplicación Android:

### 1. Configurar la URL base
```kotlin
val retrofit = Retrofit.Builder()
    .baseUrl("https://tu-servidor:5001/api/")
    .addConverterFactory(GsonConverterFactory.create())
    .build()
```

### 2. Crear interfaces de API
```kotlin
interface RiegoApiService {
    @POST("auth/register")
    suspend fun register(@Body request: RegisterRequest): LoginResponse

    @POST("auth/login")
    suspend fun login(@Body request: LoginRequest): LoginResponse

    @GET("auth/profile")
    suspend fun getProfile(@Header("Authorization") token: String): UserDto
}
```

### 3. Guardar el token
```kotlin
val token = loginResponse.token
// Guardar en SharedPreferences o DataStore
```

### 4. Usar el token en solicitudes protegidas
```kotlin
val authorization = "Bearer $token"
apiService.getProfile(authorization)
```

## ??? Estructura de carpetas

```
ServidorRiego/
??? Controllers/        # Endpoints REST
??? Services/          # Lógica de negocio
??? Data/              # DbContext y base de datos
??? Models/            # Entidades (User)
??? Dtos/              # Data Transfer Objects
??? Migrations/        # Migraciones de Entity Framework
??? Program.cs         # Configuración de la app
??? appsettings.json   # Configuración
??? README.md          # Documentación completa
```

## ?? Seguridad

**Checklist de seguridad:**
- ? Contraseñas hasheadas con BCrypt (no están almacenadas en texto plano)
- ? Tokens JWT con expiración
- ? Validación de entrada en todos los endpoints
- ? HTTPS habilitado
- ? CORS configurado de forma segura

**Para producción, además debes:**
- [ ] Cambiar la clave JWT a una más larga y compleja
- [ ] Usar HTTPS con certificado válido
- [ ] Restringir CORS a dominios específicos
- [ ] Configurar rate limiting
- [ ] Añadir logging y monitoreo
- [ ] Usar variables de entorno para secretos

## ?? Próximos Pasos

1. **Crear tabla de Dispositivos:**
   - Nuevas entidades (Dispositivo, ConfiguracionDispositivo, EstadoDispositivo)
   - Endpoints CRUD para dispositivos
   - Relación de dispositivos con usuarios

2. **Implementar control de dispositivos:**
   - Endpoints para encender/apagar riego
   - Endpoints para consultar estado
   - Sistema de historial de cambios

3. **Mejorar seguridad:**
   - Rate limiting por IP
   - Auditoría de acciones
   - Bloqueo de cuentas por intentos fallidos

4. **Adicionalidades:**
   - Swagger/OpenAPI para documentación interactiva
   - Refresh tokens
   - 2FA (autenticación de dos factores)

## ?? Solución de Problemas

### Error: "JWT Key not configured"
- Verifica que `appsettings.json` tenga configurada la clave JWT

### Error: "Access denied" en CORS
- Verifica que CORS esté habilitado en `Program.cs`

### Base de datos no se crea
- Asegúrate de tener permisos de escritura en la carpeta del proyecto

### Puerto 5001 en uso
- Cambia el puerto en `Program.cs` o usa: `dotnet run --urls https://localhost:5002`

## ?? Recursos

- [Documentación de ASP.NET Core](https://docs.microsoft.com/es-es/aspnet/core/)
- [Entity Framework Core con SQLite](https://docs.microsoft.com/en-us/ef/core/providers/sqlite/)
- [JWT en ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/jwt)

¡Éxito con tu proyecto de riego! ??
