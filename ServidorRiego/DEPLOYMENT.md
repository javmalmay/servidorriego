# ?? Guía de Despliegue - Servidor de Riego

## Desarrollo Local

### Requisitos
- .NET 9 SDK instalado
- SQL Server Express (opcional, estamos usando SQLite)

### Ejecutar en desarrollo
```bash
cd ServidorRiego
dotnet run
```

Acceder en: `https://localhost:5001`

---

## Despliegue en Producción

### 1. Preparar la aplicación

#### Compilar para release
```bash
dotnet publish -c Release -o ./publish
```

#### Verificar configuración en appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"  // Cambiar de Information a Warning
    }
  },
  "Jwt": {
    "Key": "USAR_CLAVE_SECRETA_MUY_LARGA_ALEATORIA",
    "ExpirationMinutes": 60  // Ajustar según necesidad
  }
}
```

### 2. Opciones de Hosting

#### Opción A: Windows Server con IIS

**Requisitos:**
- Windows Server 2016+
- IIS 10+
- .NET Hosting Bundle

**Instalación:**
1. Descargar .NET Hosting Bundle: https://dotnet.microsoft.com/download
2. Instalar en el servidor
3. Copiar carpeta `publish` al servidor
4. Crear Application Pool en IIS
5. Crear sitio web apuntando a la carpeta

**web.config (crear en la raíz de publish):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\ServidorRiego.exe" stdoutLogEnabled="false" hostingModel="outofprocess" />
    </system.webServer>
  </location>
</configuration>
```

#### Opción B: Docker

**Dockerfile:**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ServidorRiego.csproj", "./"]
RUN dotnet restore "ServidorRiego.csproj"
COPY . .
RUN dotnet build "ServidorRiego.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ServidorRiego.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "ServidorRiego.dll"]
```

**docker-compose.yml:**
```yaml
version: '3.8'

services:
  servidor-riego:
    build: .
    ports:
      - "5001:443"
      - "5000:80"
    environment:
      - ASPNETCORE_URLS=https://+:443;http://+:80
      - ASPNETCORE_ENVIRONMENT=Production
      - Jwt__Key=tu_clave_secreta_larga
    volumes:
      - ./data:/app/data  # Para persistencia de BD SQLite
    restart: unless-stopped
```

**Construir y ejecutar:**
```bash
docker-compose up -d
```

#### Opción C: Linux con Systemd

**Crear servicio:**
```bash
sudo nano /etc/systemd/system/servidor-riego.service
```

**Contenido:**
```ini
[Unit]
Description=Servidor de Riego
After=network.target

[Service]
Type=notify
User=www-data
WorkingDirectory=/opt/servidor-riego
ExecStart=/usr/bin/dotnet /opt/servidor-riego/ServidorRiego.dll
Restart=always
RestartSec=10
StandardOutput=journal

Environment="ASPNETCORE_URLS=https://+:443;http://+:80"
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="Jwt__Key=tu_clave_secreta_larga"

[Install]
WantedBy=multi-user.target
```

**Activar servicio:**
```bash
sudo systemctl enable servidor-riego
sudo systemctl start servidor-riego
sudo systemctl status servidor-riego
```

#### Opción D: Azure App Service

1. Crear un App Service en Azure Portal
2. Configurar deployment desde GitHub o local
3. Configuar variables de entorno en Configuration

```
Jwt__Key = tu_clave_secreta_larga
ASPNETCORE_ENVIRONMENT = Production
```

#### Opción E: Heroku

```bash
# Instalar Heroku CLI
heroku login
heroku create tu-app-name
git push heroku main
```

### 3. Certificado SSL/TLS

#### Para Desarrollo
```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

#### Para Producción

**Opción 1: Let's Encrypt (Recomendado - Gratuito)**
```bash
# En Ubuntu/Debian
sudo apt-get install certbot python3-certbot-nginx
sudo certbot certonly --standalone -d tudominio.com
```

**Opción 2: Certificado comercial**
- Comprar en providers como Sectigo, DigiCert, etc.
- Instalar en el servidor

**Configurar en appsettings:**
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:443",
        "Certificate": {
          "Path": "/path/to/cert.pfx",
          "Password": "tu_contraseña"
        }
      }
    }
  }
}
```

### 4. Configurar Firewall

```bash
# Ubuntu/Debian con UFW
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

### 5. Proxy Inverso (Nginx)

**Configuración Nginx:**
```nginx
server {
    listen 80;
    server_name tudominio.com;
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name tudominio.com;

    ssl_certificate /etc/letsencrypt/live/tudominio.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/tudominio.com/privkey.pem;

    # Configuraciones SSL recomendadas
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

### 6. Backup de Base de Datos

**Script de backup automático (Linux):**
```bash
#!/bin/bash
# backup.sh
BACKUP_DIR="/backups/servidor-riego"
DATE=$(date +%Y%m%d_%H%M%S)
cp /opt/servidor-riego/riego.db $BACKUP_DIR/riego_$DATE.db.bak
find $BACKUP_DIR -name "*.bak" -mtime +7 -delete  # Mantener 7 días
```

**Cron job:**
```bash
0 3 * * * /usr/local/bin/backup.sh  # Diariamente a las 3 AM
```

### 7. Monitoreo y Logs

#### Con Serilog (Opcional)
```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.File
```

En Program.cs:
```csharp
builder.Host.UseSerilog((context, logger) =>
{
    logger.MinimumLevel.Information()
        .WriteTo.File("logs/app-.txt", rollingInterval: RollingInterval.Day)
        .Enrich.FromLogContext();
});
```

### 8. Monitoreo de Rendimiento

```bash
# Ver procesos de dotnet
ps aux | grep dotnet

# Ver uso de puertos
netstat -tulpn | grep LISTEN

# Ver logs systemd
journalctl -u servidor-riego -f
```

### 9. Actualizar a Nueva Versión

```bash
# Compilar
dotnet publish -c Release -o ./publish

# Detener servicio
sudo systemctl stop servidor-riego

# Backup de BD
cp /opt/servidor-riego/riego.db /backups/riego_backup.db

# Copiar nuevos archivos
sudo cp -r publish/* /opt/servidor-riego/

# Iniciar servicio
sudo systemctl start servidor-riego

# Verificar
sudo systemctl status servidor-riego
```

### 10. Troubleshooting en Producción

**Puerto ya en uso:**
```bash
# Cambiar puerto en appsettings
"Kestrel": {
    "Endpoints": {
        "Http": {"Url": "http://+:5000"},
        "Https": {"Url": "https://+:5001"}
    }
}
```

**Permisos de archivo:**
```bash
sudo chown -R www-data:www-data /opt/servidor-riego
sudo chmod -R 755 /opt/servidor-riego
```

**Ver logs de errores:**
```bash
journalctl -u servidor-riego -e --lines=100
```

---

## Checklist Pre-Producción

- [ ] Cambiar JWT Key en appsettings.json
- [ ] Configurar certificado SSL/TLS válido
- [ ] Restringir CORS a dominios específicos
- [ ] Configurar backup automático de BD
- [ ] Configurar logging y monitoreo
- [ ] Probar endpoints desde Android
- [ ] Verificar HTTPS funciona correctamente
- [ ] Configurar firewall
- [ ] Crear plan de recuperación ante desastres
- [ ] Documentar credenciales de administración

---

## Monitoreo Post-Despliegue

1. Verificar que el servicio está corriendo
2. Probar endpoints de autenticación
3. Revisar logs regularmente
4. Monitorear uso de CPU y memoria
5. Verificar conectividad desde aplicación Android

---

**Última actualización**: 2024
**Versión de .NET**: 9.0
