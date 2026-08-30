# Servicio de Riego - API Authentication

## Descripción

Este es un servicio backend para controlar dispositivos de riego desde una aplicación Android. El servicio incluye:

- ? Base de datos SQLite con gestión de usuarios
- ? Autenticación con JWT (JSON Web Tokens)
- ? Endpoints para login y registro
- ? Sistema de seguridad con contraseñas hasheadas (BCrypt)
- ? Gestión de CORS para la aplicación Android

## Estructura del Proyecto

```
ServidorRiego/
??? Models/
?   ??? User.cs                 # Entidad de usuario
??? Data/
?   ??? RiegoDbContext.cs       # DbContext de Entity Framework
??? Dtos/
?   ??? AuthDtos.cs             # Data Transfer Objects
??? Services/
?   ??? AuthService.cs          # Lógica de autenticación
??? Controllers/
?   ??? AuthController.cs       # Endpoints de autenticación
??? Migrations/
?   ??? 20240101000000_InitialCreate.cs
?   ??? RiegoDbContextModelSnapshot.cs
??? GlobalUsings.cs             # Using globals
??? Program.cs                  # Configuración de la aplicación
??? appsettings.json           # Configuración
??? ServidorRiego.csproj       # Archivo de proyecto
```

## Configuración

### 1. Cambiar la clave JWT en `appsettings.json`

**IMPORTANTE**: Debes cambiar la clave JWT por una más segura. La clave actual es solo un ejemplo:

```json
{
  "Jwt": {
    "Key": "tu_clave_secreta_super_larga_de_al_menos_32_caracteres_para_jwt",
    "Issuer": "ServidorRiego",
    "Audience": "AndroidApp",
    "ExpirationMinutes": 60
  }
}
```

**Generar una clave segura:**

Ejecuta en PowerShell:
```powershell
[Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((New-Guid).Guid + (New-Guid).Guid + (New-Guid).Guid))
```

Luego reemplaza el valor de `Key` con el resultado.

### 2. Base de datos

La base de datos SQLite se crea automáticamente al ejecutar la aplicación. Se generará un archivo `riego.db` en la carpeta raíz.

**Cambiar la ubicación de la BD:** Edita en `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=ruta/a/tu/base/datos.db"
}
```

## Endpoints de Autenticación

### 1. Registro de Usuario
**POST** `/api/auth/register`

Cuerpo de la solicitud:
```json
{
  "username": "usuario123",
  "email": "usuario@example.com",
  "password": "MiContraseña123!"
}
```

Respuesta exitosa (201):
```json
{
  "success": true,
  "message": "Registro exitoso",
  "user": {
    "id": 1,
    "username": "usuario123",
    "email": "usuario@example.com",
    "createdAt": "2024-01-15T10:30:00Z"
  }
}
```

### 2. Login
**POST** `/api/auth/login`

Cuerpo de la solicitud:
```json
{
  "username": "usuario123",
  "password": "MiContraseña123!"
}
```

Respuesta exitosa (200):
```json
{
  "success": true,
  "message": "Login exitoso",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": 1,
    "username": "usuario123",
    "email": "usuario@example.com",
    "createdAt": "2024-01-15T10:30:00Z"
  }
}
```

### 3. Obtener Perfil de Usuario (Protegido)
**GET** `/api/auth/profile`

**Encabezados requeridos:**
```
Authorization: Bearer {token}
```

Respuesta (200):
```json
{
  "id": 1,
  "username": "usuario123",
  "email": "usuario@example.com",
  "createdAt": "2024-01-15T10:30:00Z"
}
```

## Ejecución

### Desarrollo
```bash
dotnet run
```

La aplicación se ejecutará en `https://localhost:5001` (HTTPS) por defecto.

### Producción
```bash
dotnet publish -c Release
```

## Paquetes NuGet Utilizados

- **Microsoft.EntityFrameworkCore.Sqlite** (9.0.0) - Base de datos SQLite
- **Microsoft.EntityFrameworkCore.Tools** (9.0.0) - Herramientas EF Core
- **Microsoft.AspNetCore.Authentication.JwtBearer** (9.0.0) - Autenticación JWT
- **BCrypt.Net-Next** (4.0.3) - Hash de contraseñas
- **Microsoft.AspNetCore.OpenApi** (9.0.16) - OpenAPI/Swagger

## Base de Datos - Esquema

### Tabla: Users

| Columna | Tipo | Descripción |
|---------|------|-------------|
| Id | INTEGER | Identificador único (PK) |
| Username | TEXT | Nombre de usuario (Único, Máx 50 caracteres) |
| Email | TEXT | Email del usuario (Único, Máx 100 caracteres) |
| PasswordHash | TEXT | Hash de la contraseña |
| CreatedAt | TEXT | Fecha de creación |
| LastLogin | TEXT | Última vez que inició sesión |
| IsActive | INTEGER | Estado activo/inactivo |

## Próximos Pasos

Para expandir el servicio puedes:

1. **Crear tablas para dispositivos:**
   ```csharp
   public class Dispositivo
   {
       public int Id { get; set; }
       public string Nombre { get; set; }
       public int UserId { get; set; }
       public bool Activo { get; set; }
       // ... propiedades adicionales
   }
   ```

2. **Crear endpoints para controlar dispositivos:**
   - `GET /api/dispositivos` - Listar dispositivos del usuario
   - `POST /api/dispositivos` - Crear nuevo dispositivo
   - `PUT /api/dispositivos/{id}` - Actualizar dispositivo
   - `DELETE /api/dispositivos/{id}` - Eliminar dispositivo

3. **Añadir validación de datos** más completa en los DTOs

4. **Implementar logging** para auditoría

5. **Añadir rate limiting** para proteger contra ataques

## Seguridad

- ? Contraseñas hasheadas con BCrypt
- ? Autenticación con JWT
- ? Validación de token en cada solicitud protegida
- ? CORS habilitado para aplicación Android
- ? Token con expiración configurable (60 minutos por defecto)

## Troubleshooting

### La base de datos no se crea
- Verifica que haya permisos de escritura en la carpeta del proyecto

### Error: "JWT Key not configured"
- Asegúrate de que `Jwt:Key` esté configurado en `appsettings.json`

### Token expirado
- El token JWT expira cada 60 minutos por defecto, requiere nuevo login

## Licencia

Este proyecto es de código abierto y puede ser modificado según tus necesidades.
