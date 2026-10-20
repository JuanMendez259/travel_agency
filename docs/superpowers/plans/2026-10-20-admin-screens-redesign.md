# Rediseño de pantallas de administración — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reproducir en las cinco pantallas de administración de la app MAUI el diseño aprobado en Stitch, sin cambiar la lógica de negocio.

**Architecture:** Un `ContentView` compartido (`AdminPageHeader`) para cabecera, navegación de vuelta y Salir; estilos con `x:Key` en `Styles.xaml` para tarjetas, botones y chips; el resto del layout se escribe inline en cada página. La barra de Shell se oculta y cada página dibuja su cabecera.

**Tech Stack:** .NET MAUI 10 (XAML + code-behind C#), Shell, tokens de `Colors.xaml`, fuente OpenSans, emoji como iconos.

**Spec:** `docs/superpowers/specs/2026-10-20-admin-screens-redesign-design.md`

## Global Constraints

- Textos de UI en español, con acentos correctos y sin typos.
- Iconos con emoji; no hay fuente de iconos en el proyecto.
- Solo tokens de `Resources/Styles/Colors.xaml`; únicas excepciones: `#DBB92A` (asiento ocupado) y colores de estado ya usados hoy.
- Sin comentarios en XAML ni en C#.
- No tocar la API, `TravelAgency.Shared`, migraciones ni rutas de `AppShell`.
- No inventar datos: lo que no existe o viene vacío se oculta.
- Títulos de sección con `SemanticProperties.HeadingLevel`; botones que solo muestran un emoji llevan `SemanticProperties.Description`; áreas táctiles de al menos 44×44.
- No ejecutar `tools/uitest`.
- **No hacer commit ni push sin confirmación explícita del usuario en ese momento.** Los pasos de commit del plan solo se ejecutan con esa confirmación.
- Validación de cada tarea: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst` sin errores.
- Pruebas unitarias: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj`.

## Review Focus

- **Navegación sin pila:** abrir una página de detalle directamente (p. ej. `scancheckin`) y pulsar ← debe llevar a `//admintrips`, no dejar una pantalla muerta.
- **Safe area:** el header no debe quedar bajo el notch ni la barra de estado en iOS/MacCatalyst.
- **Rol coordinador:** tras el rediseño no deben aparecer acciones de administrador (pagos, mapa, auditoría, editar, pausar, eliminar, opciones, unidad).
- **Filas construidas en C#:** las filas de pasajeros y asientos se crean en code-behind; al reescribir el XAML deben conservar colores, badges y tamaños.
- **Datos ausentes:** viaje sin foto, sin teléfono, sin `ChildPrice` o sin `QrToken` no debe romper el layout ni mostrar huecos vacíos.
- **Pantallas pequeñas:** el detalle y el mapa de dos pisos deben seguir siendo desplazables sin recortes.

---

### Task 1: Helper de iniciales con pruebas

**Files:**
- Create: `src/TravelAgency.App/Modules/Admin/AdminDisplay.cs`
- Test: `tests/TravelAgency.App.Tests/AdminDisplayTests.cs`

**Interfaces:**
- Produces: `public static class TravelAgency.App.Modules.Admin.AdminDisplay` con
  `public static string Initials(string? name)`.

- [ ] **Step 1: Write the failing test**

```csharp
using TravelAgency.App.Modules.Admin;
using Xunit;

namespace TravelAgency.App.Tests;

public class AdminDisplayTests
{
    [Theory]
    [InlineData("María López", "ML")]
    [InlineData("Jorge Ramírez", "JR")]
    [InlineData("Ana", "A")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void Initials_ReturnsUpToTwoLetters(string? name, string expected)
    {
        Assert.Equal(expected, AdminDisplay.Initials(name));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj --filter AdminDisplayTests`
Expected: FAIL de compilación porque `AdminDisplay` no existe.

- [ ] **Step 3: Implement `AdminDisplay.Initials`**

Archivo `src/TravelAgency.App/Modules/Admin/AdminDisplay.cs`, namespace
`TravelAgency.App.Modules.Admin`. Regla: separar por espacios, tomar la primera letra
de la primera y la última palabra, mayúsculas, `"?"` si no hay letras.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj --filter AdminDisplayTests`
Expected: PASS (5 casos).

- [ ] **Step 5: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/AdminDisplay.cs tests/TravelAgency.App.Tests/AdminDisplayTests.cs
git commit -m "admin: helper de iniciales para avatares"
```

---

### Task 2: `AdminPageHeader` y estilos compartidos

**Files:**
- Create: `src/TravelAgency.App/Controls/Admin/AdminPageHeader.xaml`
- Create: `src/TravelAgency.App/Controls/Admin/AdminPageHeader.xaml.cs`
- Modify: `src/TravelAgency.App/Resources/Styles/Styles.xaml` (añadir claves al final, sin alterar las existentes)

**Interfaces:**
- Produces: `AdminPageHeader` con propiedades enlazables `Eyebrow` (string),
  `PageTitle` (string), `ShowBack` (bool, default `false`), `ShowLogout` (bool,
  default `true`); eventos `BackRequested` y `LogoutRequested`.
- Produces estilos `AdminCard`, `AdminCardTitle`, `AdminTonalButton`,
  `AdminOutlineButton`, `AdminChip`, `AdminBadge`.

- [ ] **Step 1: Crear `AdminPageHeader.xaml`**

`ContentView` con `x:Class="TravelAgency.App.Controls.Admin.AdminPageHeader"` y
`x:Name="Root"`. Grid de tres columnas (`Auto,*,Auto`), padding `16,12,16,8`, fondo
`Surface`: columna 0 botón ← (emoji, 44×44, fondo transparente, sin borde, texto
`Primary`, `SemanticProperties.Description="Volver"`, `IsVisible="{Binding ShowBack,
Source={x:Reference Root}}"`); columna 1 `VerticalStackLayout` con eyebrow (11,
negrita, `Primary`, mayúsculas, `Text="{Binding Eyebrow, Source={x:Reference Root}}"`)
y título (26, negrita, `OnSurface`, `HeadingLevel=Level1`, `Text="{Binding PageTitle,
Source={x:Reference Root}}"`); columna 2 botón “Salir” (`AdminTonalButton`,
`IsVisible="{Binding ShowLogout, Source={x:Reference Root}}"`). Los `Clicked` llaman
a `OnBackClicked` y `OnLogoutClicked` del code-behind.

- [ ] **Step 2: Crear `AdminPageHeader.xaml.cs`**

`public partial class AdminPageHeader : ContentView` con las cuatro
`BindableProperty`, los dos eventos `EventHandler?` y:
`OnBackClicked` → si `Shell.Current.Navigation.NavigationStack.Count > 1`
ejecuta `await Shell.Current.GoToAsync("..")`; si no, `await Shell.Current.GoToAsync("//admintrips")`.
`OnBackClicked` → si `BackRequested` tiene suscriptores, lo invoca (la página navega);
si no, navega él mismo: `GoToAsync("..")` cuando hay pila y `GoToAsync("//admintrips")`
cuando no. `OnLogoutClicked` → `LogoutRequested?.Invoke(this, EventArgs.Empty)`.

- [ ] **Step 3: Añadir estilos a `Styles.xaml`**

- `AdminCard`: `TargetType="Border"`, `StrokeThickness=0`,
  `StrokeShape="RoundRectangle 16"`, `BackgroundColor=SurfaceContainerLowest`,
  `Padding=16`, `Shadow` opacidad 0.05, radio 10, offset `0,2`.
- `AdminCardTitle`: `TargetType="Label"`, 15, negrita, `OnSurface`.
- `AdminTonalButton`: `TargetType="Button"`, fondo `SurfaceContainerHigh`, texto
  `OnSurface`, `MinimumHeightRequest=44`, `CornerRadius=16`.
- `AdminOutlineButton`: `TargetType="Button"`, fondo transparente, `BorderWidth=1`,
  `BorderColor=OutlineVariant`, texto `Primary`, `MinimumHeightRequest=44`.
- `AdminChip`: `TargetType="Button"`, `MinimumHeightRequest=44`, `CornerRadius=16`,
  `Padding=14,6`, `FontSize=13`, `FontAttributes=Bold`.
- `AdminBadge`: `TargetType="Label"`, 11, negrita, `Padding=8,3`,
  `BackgroundColor=SurfaceContainerLow`, `TextColor=OnSurfaceVariant`.

- [ ] **Step 4: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 5: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Controls/Admin src/TravelAgency.App/Resources/Styles/Styles.xaml
git commit -m "admin: cabecera y estilos compartidos del rediseno"
```

---

### Task 3: Viajes

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml` (reemplazo completo)
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml.cs` (solo si un handler necesita ajustar un color)

**Interfaces:**
- Consumes: `AdminPageHeader`, `AdminCard`, `AdminChip`, `AdminTonalButton`.
- Preserva: `x:Name` `TripsSearchEntry`, `ChipTripsAll`, `ChipTripsActive`,
  `ChipTripsPaused`, `Loading`, `TripsRefresh`, `TripsList`; handlers
  `OnTripsSearchTextChanged`, `OnTripsStatusChipClicked`, `OnDetailClicked`,
  `OnSeatsClicked`, `OnToggleActiveClicked`, `OnRefreshing`, `OnLogoutClicked`;
  bindings `Trip.Title`, `Trip.Destination`, `Trip.Price`, `Thumb`, `IsPaused`,
  `ScheduleText`, `TransportText`, `SoldSeats`, `AvailableSeats`, `Capacity`,
  `ActiveButtonText`, `CanManage`.

- [ ] **Step 1: Reescribir la cabecera y la estructura**

`ContentPage` con `Shell.NavBarIsVisible="False"`, `SafeAreaEdges="Container"`,
`BackgroundColor="{StaticResource Surface}"`. Eliminar `ToolbarItems`. Primera fila:
`<controls:AdminPageHeader Eyebrow="Admin · Viajes" PageTitle="Mis Viajes" ShowBack="False" />`
con `xmlns:controls="clr-namespace:TravelAgency.App.Controls.Admin"`.
`LogoutRequested="OnLogoutClicked"`.

- [ ] **Step 2: Reescribir buscador, chips y lista**

Grid `RowDefinitions="Auto,Auto,*"`:
buscador dentro de `Border Style="{StaticResource AdminCard}"` con emoji 🔍 y el
`Entry` actual; fila de chips horizontales con `Style="{StaticResource AdminChip}"`
(el activo conserva el fondo `Primary` que aplica `StyleChips`, ajustar ahí si el
nuevo estilo lo pisa); `RefreshView` + `CollectionView` con `EmptyView` actual.
Cada tarjeta usa `AdminCard` con miniatura o recuadro tonal con 🧭, título, badge
PAUSADO (fondo `ErrorContainer`, texto `OnErrorContainer`), destino, horario,
transporte, precio, contadores y botones “Ver detalle” (`AdminTonalButton`) y
“Mapa de asientos” (`Primary`), más Pausar/Reactivar visible con `CanManage`.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 4: Verificar el foco de revisión de datos ausentes**

Con un viaje sin `Thumb`, la tarjeta debe mostrar el recuadro tonal sin hueco ni
imagen rota. Verificación manual junto con el usuario; no ejecutar UI tests.

- [ ] **Step 5: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml.cs
git commit -m "admin: rediseno de la pantalla de viajes"
```

---

### Task 4: Detalle del viaje

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml` (reemplazo completo)
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml.cs` (colores de filas si hace falta)

**Interfaces:**
- Consumes: `AdminPageHeader`, `AdminCard`, `AdminCardTitle`, `AdminTonalButton`, `AdminBadge`, `AdminDisplay.Initials`.
- Preserva todos los `x:Name` actuales: `TripImage`, `TitleLabel`, `DestinationLabel`,
  `DatesLabel`, `PriceLabel`, `TransportLabel`, `StatusLabel`, `CancellationLabel`,
  `SoldLabel`, `AvailableLabel`, `CapacityLabel`, `DescriptionLabel`,
  `HotelSummaryLabel`, `OptionsHeader`, `OptionsSummaryLabel`, `OptionsLayout`,
  `ManageOptionsButton`, `CapacityRequestsHeader`, `CapacityRequestsLayout`,
  `PassengersHeader`, `PassengersLayout`, `PassengersTotalLabel`, `PaymentsButton`,
  `MapButton`, `CheckinButton`, `FinalizeButton`, `AuditButton`, `EditActions`,
  `PauseButton`, `TransportUnitHeader`, `TransportUnitImage`,
  `TransportUnitPlateLabel`, `TransportUnitEmptyLabel`, `AddTransportUnitButton`,
  `TransportUnitEditor`, `TransportImagePreview`, `TransportImageNameLabel`,
  `TransportPlateEntry`, `RatingsHeader`, `RatingsAverageLabel`, `RatingsLayout`,
  `RatingsEmptyLabel`.
- Preserva handlers: `OnBackClicked`, `OnLogoutClicked`, `OnPaymentsClicked`,
  `OnMapClicked`, `OnCheckinClicked`, `OnFinalizeClicked`, `OnAuditClicked`,
  `OnEditClicked`, `OnPauseClicked`, `OnDeleteClicked`, `OnManageOptionsClicked`,
  `OnAddTransportUnitClicked`, `OnPickTransportImageClicked`,
  `OnSaveTransportUnitClicked`, `OnCancelTransportUnitClicked`.

- [ ] **Step 1: Cabecera y hero card**

`Shell.NavBarIsVisible="False"`, `SafeAreaEdges="Container"`, fondo `Surface`,
sin `ToolbarItems`. `AdminPageHeader` con `Eyebrow="Gestión de viajes"`,
`PageTitle="Detalle del viaje"`, `ShowBack="True"`, `LogoutRequested="OnLogoutClicked"`,
`BackRequested="OnBackClicked"`. Hero card `AdminCard` con `TripImage`, tipo de
transporte con emoji, `TitleLabel`, `DestinationLabel`, `DatesLabel`,
`TransportLabel`, `PriceLabel`, `StatusLabel`, `CancellationLabel` y chips de cupo.

- [ ] **Step 2: Métricas, nota y descripción**

Tarjeta con tres columnas (`SoldLabel`, `AvailableLabel`, `CapacityLabel`), nota de
cancelación en `Border` con fondo `#E6F4EA` y texto `PrimaryContainer`, y tarjeta
“Descripción” con `DescriptionLabel`.

- [ ] **Step 3: Hotel y opciones, solicitudes y pasajeros**

Tarjeta “Hotel y opciones” con `HotelSummaryLabel`, `OptionsHeader`,
`OptionsSummaryLabel`, `OptionsLayout` y `ManageOptionsButton` tonal. Tarjeta
“Solicitudes de cupo” con `CapacityRequestsHeader` y `CapacityRequestsLayout`.
Tarjeta “Pasajeros” con `PassengersHeader`, `PassengersLayout`,
`PassengersTotalLabel`; las filas se siguen generando en `RenderPassengers` con
avatar `AdminDisplay.Initials`, badge de estado y acciones Llamar/WhatsApp.

- [ ] **Step 4: Operación**

Tarjeta “Operación” con cinco filas (icono + texto + chevron) que disparan
`OnPaymentsClicked`, `OnMapClicked`, `OnCheckinClicked`, `OnFinalizeClicked` y
`OnAuditClicked`, conservando el `IsVisible` por rol que ya aplica el code-behind.

- [ ] **Step 5: Administración y unidad**

Tarjeta “Administración” con `EditActions` (Editar, `PauseButton`, Eliminar).
Tarjeta “Información de la unidad” con `TransportUnitImage`, placa, vacío,
`AddTransportUnitButton` y el editor completo (`TransportUnitEditor`,
`TransportImagePreview`, `TransportImageNameLabel`, `TransportPlateEntry`).

- [ ] **Step 6: Opiniones y cierre**

Tarjeta “Opiniones de clientes” con `RatingsAverageLabel`, `RatingsLayout` y
`RatingsEmptyLabel`. Cierre con “Volver a mis viajes” (`AdminOutlineButton`).

- [ ] **Step 7: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 8: Verificar rol y datos ausentes**

Entrar como coordinador y comprobar que Administración, pagos, mapa, auditoría y los
botones de administrar opciones / agregar unidad no aparecen, que Check-in / Salida
sí, y que las tarjetas informativas de opciones y unidad se mantienen como hoy
(visibles, sin acciones). Con un pasajero sin teléfono no debe quedar un botón vacío,
y sin `ChildPrice` no debe mostrarse una línea de tarifa de niño. Verificación manual.

- [ ] **Step 9: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml.cs
git commit -m "admin: rediseno del detalle del viaje"
```

---

### Task 5: Check-in / Salida

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml` (reemplazo completo)
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml.cs` (colores y estilos de filas)

**Interfaces:**
- Consumes: `AdminPageHeader`, `AdminCard`, `AdminCardTitle`, `AdminBadge`, `AdminDisplay.Initials`.
- Preserva `x:Name`: `TripTitleLabel`, `TripMetaLabel`, `StatusBanner`, `StatusLabel`,
  `ProgressLabel`, `ScanButton`, `OpenCheckinButton`, `CloseCheckinButton`,
  `CompleteButton`, `ReopenButton`, `PassengersLayout`, `NoPassengersLabel`.
- Preserva handlers: `OnScanClicked`, `OnOpenCheckinClicked`,
  `OnCloseCheckinClicked`, `OnCompleteClicked`, `OnReopenClicked`,
  `OnBackClicked`, `OnLogoutClicked`, `OnPassengerBoardToggledAsync`,
  `OnViewPassengerQrClickedAsync`, `OnBoardToggledAsync`, `OnViewQrClickedAsync`.

- [ ] **Step 1: Cabecera y datos del viaje**

`Shell.NavBarIsVisible="False"`, `SafeAreaEdges="Container"`, fondo `Surface`.
`AdminPageHeader` con `Eyebrow="Gestión de viajes"`, `PageTitle="Check-in / Salida"`,
`ShowBack="True"`, eventos de volver y salir. Tarjeta con `TripTitleLabel` y
`TripMetaLabel`.

- [ ] **Step 2: Estado y acciones**

`StatusBanner` pasa a tarjeta con fondo `Primary`, `StatusLabel` y `ProgressLabel`
en blanco, más tres pills (pasajeros, asientos, pendientes) alimentadas por
`RenderStatus` con los mismos valores que hoy. Botonera: `ScanButton` primario,
`OpenCheckinButton` / `CloseCheckinButton` / `CompleteButton` / `ReopenButton` con
los estilos tonales y la misma visibilidad.

- [ ] **Step 3: Lista de pasajeros**

Encabezado “Pasajeros” con conteo. `PassengersLayout` conserva las filas que genera
`RenderPassengers`, reestilizadas: avatar con iniciales, nombre, rol (titular si es
el primer pasajero de la reserva; acompañante el resto), asientos, total, hora de
`CheckedInAt` cuando exista, badge, `Switch` y botón QR solo si hay `QrToken`.
`NoPassengersLabel` estilizado. Nota informativa al pie.

- [ ] **Step 4: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 5: Verificar el foco de revisión de filas dinámicas**

Con una reserva de varios pasajeros, comprobar que cada fila conserva avatar,
badge, switch y QR, y que abordar uno no cambia el estado de los demás.
Verificación manual.

- [ ] **Step 6: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml.cs
git commit -m "admin: rediseno de check-in y salida"
```

---

### Task 6: Escanear QR

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml` (reemplazo completo)
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml.cs` (solo si cambia algún nombre o color)

**Interfaces:**
- Consumes: `AdminPageHeader`, `AdminCard`, `AdminOutlineButton`.
- Preserva `x:Name`: `TripLabel`, `HintLabel`, `ScannerView`, `ResultLabel`,
  `DetailLabel`, `ManualTokenEntry`, `ResumeButton`.
- Preserva `xmlns:zxing` y `BarcodesDetected="OnBarcodesDetected"`.
- Preserva handlers: `OnBarcodesDetected`, `OnManualVerifyClicked`,
  `OnResumeClicked`, `OnAppearing`, `OnDisappearing`.

- [ ] **Step 1: Cabecera, contexto y cámara**

`Shell.NavBarIsVisible="False"`, `SafeAreaEdges="Container"`, fondo `Surface`.
`AdminPageHeader` con `Eyebrow="Check-in"`, `PageTitle="Escanear QR"`,
`ShowBack="True"`, `ShowLogout="False"`; el header resuelve la vuelta por sí mismo
(`..` o `//admintrips`), la página no define `BackRequested`. Tarjeta de contexto
con 🧭, `TripLabel` y destino
(`HintLabel`). Área de cámara con `ScannerView` a sangre y cuatro esquinas dibujadas
con `Border` de 28×28 y `StrokeThickness=3`, badge “Cámara activa”.

- [ ] **Step 2: Resultado y verificación manual**

Panel con `ResultLabel` y `DetailLabel`. Tarjeta “Verificación manual” con
`ManualTokenEntry`, botón Verificar (`AdminOutlineButton`) y texto de ayuda.
`ResumeButton` tonal, visible según la lógica actual. Nota de seguridad y pie
“Solo personal autorizado · Los escaneos quedan registrados en el viaje”.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 4: Verificar el foco de revisión de navegación sin pila**

Abrir `//scancheckin` directamente con un viaje y pulsar ←: debe terminar en
“Mis Viajes” sin excepción. Verificación manual.

- [ ] **Step 5: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml.cs
git commit -m "admin: rediseno del escaner QR"
```

---

### Task 7: Mapa de asientos

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml` (reemplazo completo, conservando nombres)
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml.cs` (solo estilos de filas)

**Interfaces:**
- Consumes: `AdminPageHeader`, `AdminCard`, `AdminCardTitle`, `AdminBadge`.
- Preserva `x:Name`: `TripLabel`, `SummaryLabel`, `FloorBadgeLabel`, `FloorSelector`,
  `Floor1Button`, `Floor2Button`, `RowsContainer`, `OccupiedCountLabel`,
  `OccupiedList`, `Loading`, `SeatsRefresh`.
- Preserva handlers: `OnRefreshing`, `OnFloor1Clicked`, `OnFloor2Clicked`,
  `OnLogoutClicked`.
- Preserva métodos: `LoadSeatsAsync`, `RenderMap`, `RenderMapRows`, `BuildSeat`,
  `FloorOf`, `SelectFloor`, `UpdateFloorButtons`.

- [ ] **Step 1: Cabecera y tarjeta del viaje**

`AdminPageHeader` con `Eyebrow="Operación"`, `PageTitle="Mapa de asientos"`,
`ShowBack="True"`; sin `BackRequested`, el header resuelve la vuelta con su fallback,
y `LogoutRequested="OnLogoutClicked"`. Tarjeta `Primary` con 🚌, `TripLabel`,
`SummaryLabel` (“N ocupados · M libres”) y segunda línea de cupo por piso cuando
`HasTwoFloors`.

- [ ] **Step 2: Leyenda y distribución**

Leyenda Libre / Ocupado / Check-in con los mismos colores. Tarjeta “Distribución de
asientos” con `FloorBadgeLabel`, `FloorSelector`, `Floor1Button`, `Floor2Button`,
cabina, etiquetas “Fila izq. / ↕ / Fila der.”, `RowsContainer`, maletero y sanitario.
La etiqueta central del pasillo es `↕`, nunca “Pasillo”, para no desbordar la columna.

- [ ] **Step 3: Detalle de ocupación**

Tarjeta con `OccupiedCountLabel` y `OccupiedList`; cada fila muestra estado (✓ o •),
nombre, “Piso N · Asiento N” y badge “Check-in hecho” / “Pasajero registrado · falta
check-in”. Sin folio de reserva.

- [ ] **Step 4: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 5: Verificar el foco de revisión de pantallas pequeñas**

Con un autobús de dos pisos, alternar Piso 1 / Piso 2 y comprobar que el mapa no se
recorta, que la cabecera no tapa contenido y que el detalle de ocupación lista los
asientos del piso correcto. Verificación manual.

- [ ] **Step 6: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml.cs
git commit -m "admin: alinear el mapa de asientos con el diseno aprobado"
```

---

### Task 8: Barrido final y validación

**Files:**
- Modify: cualquiera de las cinco páginas si el barrido encuentra restos.

- [ ] **Step 1: Buscar restos del diseño anterior**

Run: `grep -rn "Gray200\|Gray100\|#EEEEEE\|#F4F4F4" src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml`
Expected: sin resultados.

- [ ] **Step 2: Revisar cabeceras**

Run: `grep -rn "NavBarIsVisible\|AdminPageHeader" src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml`
Expected: las cinco ocultan la barra y usan el header compartido.

- [ ] **Step 3: Revisar accesibilidad**

Run: `grep -rn "HeadingLevel\|SemanticProperties.Description" src/TravelAgency.App/Controls/Admin src/TravelAgency.App/Modules/Admin/Views/AdminTripsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminCheckinPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminScanQrPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml`
Expected: el header y los títulos de sección tienen `HeadingLevel`; los botones
solo-emoji tienen `Description`.

- [ ] **Step 4: Pruebas y compilación**

Run: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj`
Expected: todas pasan.

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

- [ ] **Step 5: Revisión visual del usuario**

Pedir al usuario que abra las cinco pantallas en la app y confirme que coinciden con
los mocks y que el header respeta el safe area superior (no queda bajo el notch ni
la barra de título de MacCatalyst). No ejecutar `tools/uitest`.

- [ ] **Step 6: Commit** (solo con confirmación del usuario)

```bash
git add src/TravelAgency.App/Modules/Admin/Views
git commit -m "admin: barrido final del rediseno"
```
