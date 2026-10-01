# AGENTS.md

Agente de IA que trabaja en este repositorio. Lee este archivo antes de tocar código.

## Regla más importante: nunca hagas push sin confirmación

**Cada `git push` a `main` dispara el deploy automático a producción.** Por eso:

- **NO hagas `git commit` ni `git push` hasta que el usuario confirme explícitamente** ("haz commit y push", "confirmo", etc.).
- Una confirmación previa **no** autoriza commits futuros: pide confirmación de nuevo en cada tarea.
- Cuando termines un cambio, resume lo hecho y **pregunta** si lo apruebas para commit y push.
- Si necesitas commitear para que algo tenga efecto (por ejemplo una migración que corre al arrancar la API), dilo explícitamente y espera.
- Nunca uses `git commit --amend`, `push --force` ni modifiques la config de git.
- Ante la duda, deja los cambios en el working tree.

## Estructura

```
src/TravelAgency.App       Cliente .NET MAUI (Android, iOS, MacCatalyst, Windows)
src/TravelAgency.Api       API ASP.NET Core Minimal API
src/TravelAgency.Shared    Modelos y reglas de negocio compartidas por app y API
```

La API expone todo en `Program.cs` (un único archivo). Al arrancar ejecuta migraciones
 idempotentes `Ensure*ColumnAsync` / `Ensure*TableAsync` que agregan columnas y tablas
 que faltan en la base real. Si agregas un campo a un modelo que existe en producción,
 también necesitas su `Ensure*` o el arranque falla con `InvalidOperationException`
 ("no existe la columna"). Patrón: consultar solo IDs con `Select` y luego `ExecuteSqlRawAsync`
 en vez de materializar la entidad completa (ver `EnsureUserQrColumnAsync`).

Base de datos: Supabase/Postgres en producción, SQLite como fallback local.

## Comandos

```bash
# Validar la app (MacCatalyst; android no compila local por falta de SDK)
dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst

dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj
```

API base URL: `https://travelagency-production-f6cf.up.railway.app`
(fijada en `ApiService.BaseUrl`).

## Convenciones

- **Textos de UI en español**, sin acentos Booking ni typos; el usuario final es hispanohablante.
- **Iconos con emoji**, no con Material Symbols: el proyecto solo tiene OpenSans
  (`Resources/Fonts`), no hay fuente de iconos.
- **Paleta:** verde Material. `#003B1B` primary, `#14532D` primary-container,
  `#006C49` secondary, `#6CF8BB` secondary-container, `#FFFFFF` surface-container-lowest,
  `#F2F3FF` low, `#EAEDFF` container, `#E2E7FF` high, `#DAE2FD` highest,
  `#131B2E` on-surface, `#404941` on-surface-variant, `#717970` outline, `#BA1A1A` error.
  Se declara en `ContentPage.Resources` por vista, no hay diccionario global.
- **Nunca inventes datos** que el modelo no tiene (fechas de contraseña, tarjetas,etc.).
  Si el HTML de referencia trae datos falsos, pon texto honesto o deja la función como
  "Próximamente".
- Rutas de navegación en `AppShell.xaml.cs`; vistas de cliente en
  `Modules/Client/Views/`, de admin en `Modules/Admin/Views/`.

## Reglas de negocio que ya existen

- **Tarifas de niños:** `Trip.ChildPrice` es opcional. Si es `null`, el niño paga tarifa de
  adulto. La regla vive en `BookingRefundPolicy`/API y en el cálculo de total; la app solo
  la refleja. Rango de niño: 0 a 11 años.
- **Cancelación:** el botón existe siempre que la reserva no esté cancelada y el viaje no
  haya iniciado/finalizado. `Trip.CancellationDaysLimit` es obligatorio (default
  `BookingRefundPolicy.DefaultCancellationDaysLimit` = 3). Dentro del límite el reembolso
  es del 100% de lo abonado; fuera, multa del 30% y se devuelve el 70%. La app calcula lo
  mismo que la API con `BookingRefundPolicy`, no dupliques la fórmula.

## Flujo de trabajo sugerido

1. Explora antes de editar; si algo no coincide con lo pedido, **ofrece opciones** en vez de
   adivinar.
2. Cambia lo mínimo necesario y en el estilo del archivo que ya existe.
3. Compila y reporta errores si los hay.
4. Resume los cambios y **pide confirmación** para commit y push.

## Notas de MAUI

- Compartir archivos: `Share.Default.RequestAsync(...)`.
- Fotos: revisa permisos en `Info.plist` / `AndroidManifest.xml` antes de usarlas.
- `ScrollView` con contenido de altura indefinida (mapas, listas largas) necesita
  `HeightRequest` o得 un contenedor con tamaño definido.