# ?? Casos de Prueba - Servidor de Riego

## Requisitos Previos

- Servidor ejecutándose en `https://localhost:5001`
- Herramienta HTTP client (curl, Postman, REST Client o Thunder Client)
- Base de datos vacía

## 1. Test de Registro de Usuario

### TC-001: Registro exitoso
**Objetivo**: Registrar un usuario nuevo correctamente

**Datos de entrada**:
```json
{
  "username": "testuser",
  "email": "test@example.com",
  "password": "TestPassword123!"
}
```

**Solicitud**:
```http
POST /api/auth/register HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "username": "testuser",
  "email": "test@example.com",
  "password": "TestPassword123!"
}
```

**Resultado esperado**: 
- Código: 201
- Body: `success: true, user.id: 1`
- BD: Usuario creado con PasswordHash encriptado

---

### TC-002: Registrar con usuario duplicado
**Objetivo**: Rechazar registro cuando username ya existe

**Precondición**: TC-001 ejecutado

**Datos de entrada**:
```json
{
  "username": "testuser",
  "email": "another@example.com",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Código: 400
- `success: false`
- `message: "El usuario o email ya existe"`

---

### TC-003: Registrar con email duplicado
**Objetivo**: Rechazar registro cuando email ya existe

**Datos de entrada**:
```json
{
  "username": "newuser",
  "email": "test@example.com",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Código: 400
- `success: false`

---

### TC-004: Registrar con campos vacíos
**Objetivo**: Validar campos requeridos

**Datos de entrada**:
```json
{
  "username": "",
  "email": "test@example.com",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Código: 400
- `success: false`

---

### TC-005: Registrar con email inválido
**Objetivo**: Validar formato de email

**Datos de entrada**:
```json
{
  "username": "user123",
  "email": "not-an-email",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Código: 400
- `success: false`

---

## 2. Test de Login

### TC-006: Login exitoso
**Objetivo**: Iniciar sesión con credenciales correctas

**Precondición**: TC-001 ejecutado

**Datos de entrada**:
```json
{
  "username": "testuser",
  "password": "TestPassword123!"
}
```

**Solicitud**:
```http
POST /api/auth/login HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "username": "testuser",
  "password": "TestPassword123!"
}
```

**Resultado esperado**: 
- Código: 200
- `success: true`
- `token`: JWT válido (comienza con "eyJ")
- `user.id: 1`
- BD: `LastLogin` actualizado

---

### TC-007: Login con contraseña incorrecta
**Objetivo**: Rechazar login con contraseña errónea

**Datos de entrada**:
```json
{
  "username": "testuser",
  "password": "WrongPassword"
}
```

**Resultado esperado**: 
- Código: 401
- `success: false`
- `message: "Usuario o contraseña inválidos"`

---

### TC-008: Login con usuario inexistente
**Objetivo**: Rechazar login si usuario no existe

**Datos de entrada**:
```json
{
  "username": "nonexistent",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Código: 401
- `success: false`

---

### TC-009: Login con usuario desactivado
**Objetivo**: Rechazar login si usuario está inactivo

**Precondición**: Desactivar usuario en BD (UPDATE Users SET IsActive=0 WHERE Id=1)

**Datos de entrada**:
```json
{
  "username": "testuser",
  "password": "TestPassword123!"
}
```

**Resultado esperado**: 
- Código: 401
- `success: false`
- `message: "La cuenta de usuario está desactivada"`

---

### TC-010: Login con campos vacíos
**Objetivo**: Validar campos requeridos

**Datos de entrada**:
```json
{
  "username": "",
  "password": ""
}
```

**Resultado esperado**: 
- Código: 400
- `success: false`

---

## 3. Test de Endpoints Protegidos

### TC-011: Obtener perfil con token válido
**Objetivo**: Acceder a endpoint protegido con token válido

**Precondición**: TC-006 ejecutado, obtener token

**Solicitud**:
```http
GET /api/auth/profile HTTP/1.1
Host: localhost:5001
Authorization: Bearer {token_del_tc_006}
```

**Resultado esperado**: 
- Código: 200
- Body: UserDto con datos correctos

---

### TC-012: Obtener perfil sin token
**Objetivo**: Rechazar acceso sin autenticación

**Solicitud**:
```http
GET /api/auth/profile HTTP/1.1
Host: localhost:5001
```

**Resultado esperado**: 
- Código: 401
- Mensaje de no autorizado

---

### TC-013: Obtener perfil con token inválido
**Objetivo**: Rechazar token malformado

**Solicitud**:
```http
GET /api/auth/profile HTTP/1.1
Host: localhost:5001
Authorization: Bearer invalid-token-here
```

**Resultado esperado**: 
- Código: 401

---

### TC-014: Obtener perfil con token expirado
**Objetivo**: Rechazar token vencido (simular cambio en appsettings: ExpirationMinutes=1, esperar 2 minutos)

**Resultado esperado**: 
- Código: 401
- Mensaje indicando token expirado

---

### TC-015: Obtener perfil con formato de token incorrecto
**Objetivo**: Validar formato de Authorization header

**Solicitud (falta "Bearer")**:
```http
GET /api/auth/profile HTTP/1.1
Host: localhost:5001
Authorization: {token}
```

**Resultado esperado**: 
- Código: 401

---

## 4. Test de Seguridad

### TC-016: Verificar que contraseña está hasheada
**Objetivo**: Confirmar que contraseña NO se almacena en texto plano

**Verificación BD**:
```sql
SELECT PasswordHash FROM Users WHERE Username='testuser';
```

**Resultado esperado**: 
- El valor es un hash BCrypt (comienza con $2a$ o similar)
- NO es igual a la contraseña original

---

### TC-017: Intentar múltiples logins fallidos
**Objetivo**: Simular ataque de fuerza bruta

**Procedimiento**: 
Realizar 10 intentos de login con contraseña incorrecta

**Resultado esperado**: 
- Todos rechazados con 401
- Posible rate limiting en futuros desarrollos

---

### TC-018: Verificar JWT contiene datos correctos
**Objetivo**: Decodificar JWT y verificar claims

**Procedimiento**: 
Decodificar token JWT en https://jwt.io

**Resultado esperado**: 
```json
{
  "userId": "1",
  "username": "testuser",
  "email": "test@example.com",
  "exp": 1705315000,  // Tiempo de expiración
  "iss": "ServidorRiego",
  "aud": "AndroidApp"
}
```

---

## 5. Test de Volumen

### TC-019: Registrar múltiples usuarios
**Objetivo**: Verificar rendimiento con múltiples usuarios

**Procedimiento**: 
Registrar 100 usuarios en bucle

**Tiempo esperado**: 
< 5 segundos total

**Resultado esperado**: 
- Todos con código 201
- IDs incrementales

---

### TC-020: Login múltiple simultáneo
**Objetivo**: Verificar rendimiento bajo concurrencia

**Procedimiento**: 
10 login simultáneos

**Tiempo esperado**: 
< 2 segundos

---

## 6. Test de Validación

### TC-021: Username con caracteres especiales
**Datos de entrada**:
```json
{
  "username": "user@#$%",
  "email": "user@example.com",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Aceptado (solo validar longitud 1-50 caracteres)

---

### TC-022: Username muy largo
**Datos de entrada**:
```json
{
  "username": "a".repeat(100),
  "email": "user@example.com",
  "password": "Password123!"
}
```

**Resultado esperado**: 
- Código: 400
- `success: false`

---

### TC-023: Contraseña muy corta
**Datos de entrada**:
```json
{
  "username": "user123",
  "email": "user@example.com",
  "password": "123"
}
```

**Resultado esperado**: 
- Aceptado (no hay validación de longitud de contraseña)
- Nota: Considerar agregar en futuro

---

## 7. Test de CORS

### TC-024: Solicitud CORS desde origen diferente
**Procedimiento**: 
Desde navegador en http://localhost:3000, ejecutar:
```javascript
fetch('https://localhost:5001/api/auth/login', {
  method: 'POST',
  headers: {'Content-Type': 'application/json'},
  body: JSON.stringify({username: 'test', password: 'test'})
})
```

**Resultado esperado**: 
- Response headers incluyan `Access-Control-Allow-*`
- Solicitud completada exitosamente

---

## Ejecución de Tests

### Con Curl
```bash
# TC-001: Registro
curl -X POST https://localhost:5001/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","email":"test@example.com","password":"TestPassword123!"}'

# TC-006: Login
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"TestPassword123!"}'

# TC-011: Perfil (reemplazar TOKEN)
curl -X GET https://localhost:5001/api/auth/profile \
  -H "Authorization: Bearer TOKEN"
```

### Con Postman
1. Importar colección desde `test_endpoints.http`
2. Ejecutar en orden los test cases
3. Usar variables para guardar token

### Con VS Code REST Client
1. Crear archivo `test.http`
2. Copiar ejemplos de HTTP
3. Hacer clic en "Send Request"

---

## Checklist de Pruebas

- [ ] TC-001 a TC-010: Registro y Login
- [ ] TC-011 a TC-015: Endpoints protegidos
- [ ] TC-016 a TC-018: Seguridad
- [ ] TC-019 a TC-020: Volumen y concurrencia
- [ ] TC-021 a TC-023: Validación
- [ ] TC-024: CORS
- [ ] Verificar logs en servidor
- [ ] Verificar base de datos
- [ ] Probar desde aplicación Android

---

**Documento de Pruebas**: v1.0  
**Última actualización**: 2024  
**Estado**: Listo para ejecutar
