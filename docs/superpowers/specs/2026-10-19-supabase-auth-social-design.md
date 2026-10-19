# Supabase Auth con proveedores sociales — Diseño

**Fecha:** 2026-10-19
**Rama de trabajo:** `main` (a implementar)
**Estado:** Diseño aprobado; pendiente de implementación

---

## 1. Resumen

Añadir a la app la posibilidad de **registrarse e iniciar sesión con Google, Apple y Facebook** usando **Supabase Auth**, dentro del login que ya existe. El correo/contraseña actual no cambia: la API sigue emitiendo su propio JWT y los roles (Admin/Coordinador/Cliente) siguen viviendo en la base de datos de la app.

Los usuarios que ya tienen cuenta con contraseña **no se vinculan automáticamente** al entrar con un proveedor: se les pide que usen su contraseña. Las cuentas nuevas creadas por la vía social pasan por un paso breve de perfil para capturar teléfono y contacto de emergencia.

## 2. Decisiones acordadas

| Tema | Decisión |
|---|---|
| Alcance | Social **encima** del login actual. `POST /api/auth/login` y `/api/auth/register` no se tocan. La API sigue emitiendo su JWT. |
| Proveedores | **Google, Apple, Facebook**. Instagram no es proveedor de Supabase Auth. |
| Instagram en el login | **Solo enlace de texto** que abre la página de Instagram en el navegador. No autentica. |
| Correo ya existente con contraseña | **No se vincula.** Respuesta 409 con mensaje; la app rellena el correo en el formulario de contraseña. |
| Cuenta social nueva | Se confía en el correo **ya verificado por el proveedor** (Google y Apple siempre; Facebook casi siempre). No se añade verificación propia. |
| Datos tras el primer ingreso social | Paso de perfil: **teléfono obligatorio**, contacto de emergencia opcional. No se pide foto. |
| Enfoque técnico | **Puente de sesión**: la app hace el OAuth con Supabase (PKCE) y canjea la sesión por el JWT de la app en un endpoint nuevo. Sin librerías nuevas. |

## 3. Arquitectura y flujo

### 3.1 Componentes

- **App:** servicio nuevo `SupabaseAuthService` (PKCE + `WebAuthenticator`) y paso de perfil nuevo `ProfileSetupPage`.
- **Supabase Auth:** hace el OAuth con Google/Apple/Facebook y custodia la sesión social.
- **API:** endpoint nuevo `POST /api/auth/social` que valida la sesión de Supabase y devuelve el JWT de la app.
- **Deep Link `proximaparadatours://auth`:** canal por el que el navegador regresa a la app.
- **API sigue válida para el resto:** `GET /api/trips`, reservas, check-in, etc., no cambian.

### 3.2 Flujo paso a paso

1. En el login, la persona toca "Continuar con Google" (o Apple/Facebook).
2. La app abre el navegador del sistema con la URL de autorización de Supabase, enviando el reto PKCE (`code_challenge` + `code_challenge_method=s256`) y un `state` aleatorio.
3. El proveedor autentica y Supabase redirige a `proximaparadatours://auth?code=…&state=…`.
4. La app valida que el `state` coincide con el guardado, toma el `code` y lo canjea por la sesión de Supabase en `POST {SUPABASE_URL}/auth/v1/token?grant_type=pkce` con el `code_verifier`.
5. La app llama `POST /api/auth/social` con el `access_token` de Supabase.
6. La API valida ese token contra Supabase (`GET {SUPABASE_URL}/auth/v1/user`) y obtiene `sub`, `email`, `email_confirmed_at`, `user_metadata.full_name` y `app_metadata.provider`.
7. La API decide en este orden:
   - `sub` ya vinculado → emite el JWT de la app.
   - correo existe pero sin vincular → **409** "ya tienes una cuenta: inicia sesión con tu contraseña".
   - cuenta nueva → crea el usuario Cliente (con `QrToken` nuevo) y responde JWT + `PerfilIncompleto = true`.
8. Si es cuenta nueva, la app muestra el paso de perfil (teléfono obligatorio, contacto de emergencia opcional) y lo guarda.
9. La app guarda la sesión igual que hoy y entra al shell.

El alta en la base (paso 7) solo crea **clientes**. Las cuentas Admin/Coordinador existentes caen siempre en "correo existente", así que nunca se auto-crean por la vía social.

## 4. Base de datos y API

### 4.1 `Users` (tabla existente → requiere `Ensure*`)

Dos columnas nuevas, ambas nulas:

| Columna | Tipo | Uso |
|---|---|---|
| `SupabaseUserId` | string | El `sub` (UUID) de Supabase. Es la vinculación real. Índice único. |
| `Proveedor` | string | `google`, `apple` o `facebook`. |

Al arranque, junto a los demás `Ensure*`, se añade `EnsureUserSupabaseColumnsAsync(db, provider)`. Sin ello la API no arranca en producción.

**Refuerzos opcionales incluidos** (con comprobación previa para no romper producción): índice único sobre `SupabaseUserId` y sobre `Correo`.

### 4.2 `POST /api/auth/social` (público, como `login`/`register`)

Entrada: `{ accessToken }`.

1. **Validar el token contra Supabase:** `GET {Supabase:Url}/auth/v1/user` con `apikey: <Supabase:AnonKey>` y `Authorization: Bearer accessToken`. Token inválido → **401**.
2. Leer `sub`, `email`, `email_confirmed_at`, `user_metadata.full_name` y `app_metadata.provider`.
3. **Reglas en orden:**
   - sin correo → **400** "Tu proveedor no entregó un correo."
   - `email_confirmed_at` nulo → **400** "Tu proveedor no verificó tu correo."
   - `sub` ya vinculado → emite el JWT de la app.
   - correo existe sin vincular → **409** "Ya tienes una cuenta con este correo. Inicia sesión con tu contraseña."
   - cuenta nueva → crea `Users` (Nombre y correo del proveedor, `SupabaseUserId`, `Proveedor`, `Role = Client`, `QrToken` nuevo, `Phone`/`EmergencyContact` en null) y responde **JWT + `PerfilIncompleto = true`**.

Respuesta: `SocialAuthResponse { Token, UserId, Name, Email, Role, PerfilIncompleto, Proveedor }` — extiende el `AuthResponse` actual con dos campos.

Contrato de error: la API devuelve el mensaje en el cuerpo; la app lo muestra en una alerta (contrato existente, sin código de error nuevo).

### 4.3 Editar el perfil propio

`PUT /api/users/me` — guarda `Name`, `Phone` (obligatorio) y `EmergencyContact` (opcional), con sesión de cliente. Es pieza nueva: hoy `GET /api/users/me` solo lee y `PUT /api/users/{id}` es AdminOnly para coordinadores.

### 4.4 Configuración

- **API (Railway):** nuevo `Supabase:AnonKey`. `Supabase:Url` ya existe.
- **App:** constantes estáticas `SupabaseUrl` y `SupabaseAnonKey` en `SupabaseAuthService`, con override por variable de entorno en `MauiProgram` — el mismo patrón que `ApiService.BaseUrl` / `TRAVELAGENCY_API_BASEURL`:
  - `TRAVELAGENCY_SUPABASE_URL`
  - `TRAVELAGENCY_SUPABASE_ANONKEY`

## 5. App

### 5.1 `SupabaseAuthService`

En `Services/`, junto a `ApiService` y `SessionService`. Sin librerías nuevas: `WebAuthenticator` (MAUI.Essentials) + `System.Security.Cryptography` para PKCE.

1. Genera `code_verifier` y `code_challenge` (S256, Base64Url) y un `state` aleatorio.
2. `WebAuthenticator.Default.AuthenticateAsync` con la URL de autorización de Supabase y `CallbackUrl = proximaparadatours://auth`.
3. Valida que el `state` devuelto coincide con el guardado y toma el `code`.
4. Canjea el `code` por la sesión en `POST {SUPABASE_URL}/auth/v1/token?grant_type=pkce`.
5. Llama a `POST /api/auth/social` con el `access_token`.
6. Devuelve el `AuthResponse` de la app (con `PerfilIncompleto`).

La lógica PKCE vive aquí y es fácil de probar aislada.

### 5.2 Deep Link

- **iOS** (`Platforms/iOS/Info.plist`): añadir `CFBundleURLTypes` con el esquema `proximaparadatours`.
- **Android** (`Platforms/Android/MainActivity.cs`): atributo `[IntentFilter]` sobre la actividad de MAUI con `android:scheme="proximaparadatours"` y categorías `BROWSABLE` + `DEFAULT`.
- **MacCatalyst**: reutiliza el `Info.plist` de iOS.
- En Supabase hay que dar de alta `proximaparadatours://auth` como *Allowed Redirect URL*.

### 5.3 Navegación

- Ruta nueva en `AppShell.xaml.cs`: `profilesetup` → `ProfileSetupPage`.
- `LoginPage` entra al shell como hoy **salvo** cuando la respuesta social dice `PerfilIncompleto`; entonces navega a `profilesetup`.

### 5.4 Login

- Tres botones de proveedor: **Google**, **Apple** y **Facebook**. Facebook pasa de abrir la página a autenticar.
- **Instagram**: enlace de texto discreto que abre la página en el navegador. Sin autenticación.

### 5.5 `ProfileSetupPage`

- Estilo del login, con el nombre ya venido del proveedor en modo lectura.
- Teléfono (obligatorio, 10 dígitos con `PhoneNumberFormatter`) y contacto de emergencia (opcional).
- Botón "Continuar" → `PUT /api/users/me` → entra al shell.
- No se pide foto: si el proveedor no entrega foto, no se inventa.

## 6. Casos borde y errores

| Caso | Comportamiento |
|---|---|
| Token de Supabase inválido o caducado | **401** → "No pudimos comprobar tu identidad con el proveedor. Intenta de nuevo." Sin reintentos automáticos. |
| Proveedor sin correo | **400** → "Tu proveedor no entregó un correo. Usa otro proveedor o el registro con contraseña." |
| Correo del proveedor sin verificar | **400** → "Tu proveedor no verificó tu correo. Revisa tu cuenta del proveedor y vuelve a intentarlo." |
| Correo ya existe con contraseña | **409** → "Ya tienes una cuenta con este correo. Inicia sesión con tu contraseña." y la app **rellena el correo** en el formulario de contraseña. |
| Cancelaste el navegador | `WebAuthenticator` vuelve sin `code` → "Cancelaste el inicio de sesión." y te quedas en el login. Sin crashes. |
| Error de red al canjear | Mensaje genérico + reintentar. |
| Doble pulsación en el proveedor | Botón deshabilitado mientras vuelas; el índice único de `SupabaseUserId` evita crear dos cuentas. |
| `state` no coincide | Se rechaza el callback (protección CSRF) y se vuelve al login. |
| Cuenta Admin/Coordinador | Cae siempre en "correo existente" → usan contraseña. Nunca se auto-crean por la vía social. |
| Dos logins sociales a la vez | Índice único + re-lectura tras el conflicto → mensaje amigable. |
| Cuenta social nueva que cierra la app antes de dar el teléfono | No se pierde nada; entra directo y el **perfil muestra un aviso** si falta teléfono (usando `GET /api/users/me`). Sin bloqueos extra. |

## 7. Configuración externa (manual, sin código)

Requisito previo para que los botones funcionen. Se hace en los paneles; el código no puede hacerlo.

### 7.1 Supabase

1. **Authentication → Providers**: activar **Google**, **Apple** y **Facebook** y pegar las credenciales de cada uno.
2. **Authentication → URL Configuration**: añadir `proximaparadatours://auth` a *Allowed Redirect URLs*.
3. **Project Settings → API**: copiar `anon public` → será `Supabase:AnonKey` en la app y en Railway.

### 7.2 Google Cloud

4. Crear proyecto y **pantalla de consentimiento** (Externa; añadir correos de prueba).
5. **Credenciales → cliente OAuth tipo *Web*** (Supabase exige este tipo) con *URI de redirección autorizado* `https://<REF>.supabase.co/auth/v1/callback`. Copiar ID y secreto al paso 1.

### 7.3 Apple (Apple Developer)

6. Crear un **Services ID** cuyo identificador sea `com.companyname.travelagency.app`, activar *Sign in with Apple*, indicar el App ID principal y añadir `proximaparadatours://auth` como *Return URL*.
7. Crear una **llave** con *Sign in with Apple* → descargar el `.p8` y anotar *Team ID*, *Key ID* y *Services ID*. Pegarlos en Supabase.

Nota: Apple Developer Program tiene coste (~99 USD/año). Sin Apple, Google y Facebook ya cubren la mayoría de casos; Apple solo es obligatorio en iOS una vez hay login de terceros (guía 4.8).

### 7.4 Facebook (Meta for Developers)

8. Crear la app y añadir el producto **Facebook Login**; en *Settings* poner `https://<REF>.supabase.co/auth/v1/callback` en *Valid OAuth Redirect URIs*.
9. Copiar *App ID* y *App Secret* (Settings → Basic) a Supabase.

## 8. Pruebas y despliegue

### 8.1 Unitarias (xunit, sin red)

- Generación y formato del reto PKCE (`code_verifier`/`code_challenge` S256, Base64Url).
- Validación del `state` en el callback.
- Parseo del callback: `code` presente, `code` ausente, `state` distinto.

### 8.2 Manuales (dispositivo)

- Cada proveedor (Google, Apple, Facebook).
- Cancelar el navegador → vuelve al login.
- Correo ya existente → mensaje 409 y correo rellenado.
- Proveedor sin correo → mensaje 400.
- Primer ingreso → paso de perfil → teléfono guardado (confirmar con `GET /api/users/me`).

### 8.3 Despliegue

- **API:** Railway con `Supabase:AnonKey` (mismo despliegue que hoy).
- **App:** el deep link y el botón de Apple exigen **una release nueva en las tiendas**. Sin release, la app no abre el proveedor ni vuelve de él.

## 9. Archivos que cambian

### API

- `src/TravelAgency.Api/Data/AppDbContext.cs` — mapeo de `SupabaseUserId` y `Proveedor` en `User`.
- `src/TravelAgency.Api/Program.cs` — `EnsureUserSupabaseColumnsAsync` + índices únicos; endpoint `POST /api/auth/social`; endpoint `PUT /api/users/me`; lectura de `Supabase:AnonKey`.
- `src/TravelAgency.Shared/Models/User.cs` — propiedades nuevas.
- `src/TravelAgency.Shared/Models/AuthModels.cs` — `SocialLoginRequest`, `SocialAuthResponse` (con `PerfilIncompleto`), `UpdateProfileRequest`.

### App

- `src/TravelAgency.App/Services/SupabaseAuthService.cs` — nuevo.
- `src/TravelAgency.App/Services/ApiService.cs` — `SocialAuthAsync`, `UpdateMyProfileAsync`.
- `src/TravelAgency.App/Modules/Auth/Views/ProfileSetupPage.xaml(.cs)` — nuevo.
- `src/TravelAgency.App/Modules/Auth/Views/LoginPage.xaml(.cs)` — botones de proveedor, enlace de Instagram.
- `src/TravelAgency.App/AppShell.xaml.cs` — ruta `profilesetup`.
- `src/TravelAgency.App/MauiProgram.cs` — registro del servicio.
- `src/TravelAgency.App/Platforms/iOS/Info.plist` — `CFBundleURLTypes`.
- `src/TravelAgency.App/Platforms/Android/MainActivity.cs` o `AndroidManifest.xml` — `intent-filter`.
- `src/TravelAgency.App/Services/SupabaseAuthService.cs` — constantes `SupabaseUrl`/`SupabaseAnonKey` (con override por variable de entorno).

### Tests

- `tests/TravelAgency.App.Tests/` — pruebas del PKCE, `state` y parseo del callback.

## 10. Fuera de alcance

- Vinculación automática de cuentas existentes por correo.
- Verificación de correo propia para cuentas sociales.
- Persistencia del token de refresco de Supabase en `SecureStorage` (re-login silencioso).
- Proveedores adicionales (Twitter/X, GitHub, Microsoft, etc.).
- Login social para cuentas Admin/Coordinador.
- Rediseño del login más allá de los botones de proveedor.
