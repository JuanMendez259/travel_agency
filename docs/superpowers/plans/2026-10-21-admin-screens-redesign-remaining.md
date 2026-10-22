# Rediseño de las 13 pantallas admin restantes — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplicar el lenguaje visual ya aprobado a las 13 vistas de administración pendientes, sin cambiar lógica, datos, permisos ni navegación.

**Architecture:** Se reutilizan `AdminPageHeader` y los estilos `AdminCard`, `AdminCardTitle`, `AdminTonalButton`, `AdminOutlineButton` y `AdminChip`. Cada página oculta la barra de Shell, usa fondo `Surface` y `SafeAreaEdges="Container"`. Las filas dinámicas construidas en C# se reestilizan sin alterar sus métodos de carga.

**Tech Stack:** .NET MAUI 10, XAML + code-behind, Shell, OpenSans, emoji, tokens de `Colors.xaml`.

**Spec:** `docs/superpowers/specs/2026-10-20-admin-screens-redesign-design.md`

## Global Constraints

- Textos de UI en español, con acentos correctos y sin typos.
- Iconos con emoji.
- Solo tokens de `Resources/Styles/Colors.xaml`; en C# se permiten literales equivalentes.
- Sin comentarios en XAML ni en C#.
- No tocar la API, `TravelAgency.Shared`, migraciones ni rutas de `AppShell`.
- No inventar datos: lo que no existe o viene vacío se oculta.
- Títulos de sección con `SemanticProperties.HeadingLevel`; botones que solo muestran un emoji llevan `SemanticProperties.Description`; áreas táctiles de al menos 44×44.
- No ejecutar `tools/uitest`.
- **No hacer commit ni push sin confirmación explícita del usuario en ese momento.**
- Validación de cada tarea: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst` sin errores.
- Pruebas unitarias: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj`.

## Review Focus

- Coordinador: no deben aparecer acciones exclusivas de admin.
- Navegación directa sin pila: la flecha debe volver a `//admintrips`.
- Safe area/notch en iOS y MacCatalyst.
- Formularios largos y mapas deben seguir siendo desplazables sin recortes.
- Filas dinámicas deben conservar badges, colores de estado y acciones.
- Ausencia de foto, teléfono, `ChildPrice` o `QrToken` no debe romper el layout.

---

### Task 1: `AdminBookingsPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminBookingsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminBookingsPage.xaml.cs`

**Interfaces:**
- Consumes: `AdminPageHeader`, `AdminCard`, `AdminChip`.
- Produces: misma página con los `x:Name` y eventos actuales.

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Reservas` / `Reservas`, sin flecha, con Salir. Buscador en `AdminCard`; chips Pendientes/Confirmadas/Canceladas/Todas con `AdminChip`; filtro de fecha en tarjeta; lista con tarjetas de reserva (viaje, cliente, asientos, total, badge de estado, botón Ver detalle). Conservar `SearchEntry`, `ChipPending`, `ChipConfirmed`, `ChipCancelled`, `ChipAll`, `FromDatePicker`, `ClearDateBtn`, `Loading`, `BookingsRefresh`, `BookingsList` y todos los eventos.

- [ ] **Step 2: Actualizar colores de chips en C#**

Sustituir `#E2E8F0`, `#1A202C` y `#2B6CB0` por `Primary`/`OnPrimary` y `SurfaceContainerHigh`/`OnSurface`.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 2: `AdminStatsPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminStatsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminStatsPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Estadísticas`. Cuatro KPI en `AdminCard` con emoji e icono tonal; gráfico en tarjeta; ocupación y top destinos en tarjetas. Conservar `TripsKpi`, `SeatsKpi`, `RevenueKpi`, `OccupancyKpi`, `RevenueChart`, `OccupancyList`, `DestinationsList`.

- [ ] **Step 2: Ajustar colores del gráfico**

`BarColor` → `Primary`; `LabelColor` → `Outline`; `AxisColor` → `OutlineVariant`.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 3: `AdminDashboardPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Publicar Viaje`. Formulario en tarjetas: datos, capacidad/modalidades, hotel/reservas, clasificación, visibilidad, imagen y acciones. Conservar `PageScroll`, `FormTitleLabel` y todos los `x:Name`, switches, visibilidades y eventos.

- [ ] **Step 2: Ajustar color de estado**

`#C53030` → `Error`.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 4: `AdminCoordinadoresPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminCoordinadoresPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminCoordinadoresPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Coordinadores`. Formulario en `AdminCard`; lista de coordinadores como tarjetas con avatar de iniciales, nombre, correo, teléfono y badges. Conservar `CoordinatorsLayout`, `PasswordFieldsLayout`, `FeedbackLabel`, `CountLabel`, `EmptyLabel`, `LoadingIndicator` y eventos.

- [ ] **Step 2: Reestilizar filas dinámicas**

Mantener los datos y acciones actuales; usar tarjetas, badges y botones de 44×44.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 5: `AdminDiscountsPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDiscountsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDiscountsPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Descuentos`. Formulario en tarjetas: código/tipo, valor, vigencia, alcance, límite y estado. Lista como tarjetas con badge Activo/Inactivo y acciones. Conservar `VigenciaFields`, `VigenciaSwitch`, pickers, `DiscountsLayout`, `EmptyLabel`, `LoadingIndicator` y eventos.

- [ ] **Step 2: Reestilizar filas dinámicas**

Mantener datos y acciones actuales; badges y botones de 44×44.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 6: `AdminPayoutsPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminPayoutsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminPayoutsPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Reembolsos`. Introducción en tarjeta y contador (`CountLabel`). Filas dinámicas como tarjetas con cliente, monto, método, nota, fecha, badge de estado y botones Pagar/Rechazar. Conservar `PayoutsLayout`, `EmptyLabel`, `ErrorLabel`, `LoadingIndicator`.

- [ ] **Step 2: Reestilizar filas dinámicas**

Mantener la paleta de estados existente y las acciones actuales.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 7: `AdminTripExpensesPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripExpensesPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripExpensesPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Admin · Gastos`. Selector de viaje y resumen de inversión en tarjeta; formulario en tarjeta; lista de gastos en tarjeta; total en `SecondaryContainer`. Conservar `TripPicker`, `TripTitleLabel`, `ConceptEntry`, `AmountEntry`, `AddButton`, `CancelEditButton`, `ExpensesLayout`, `TotalLabel`, `LoadingIndicator` y eventos.

- [ ] **Step 2: Reestilizar filas dinámicas**

Mantener datos y acciones actuales; tarjetas y botones de 44×44.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 8: `AdminBookingDetailPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminBookingDetailPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminBookingDetailPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Reservas` / `Detalle de reserva`, flecha y Salir. Hero card con viaje, cliente, información, badge de estado y saldo. Necesidades especiales en `WarningContainer`. Acciones: Registrar pago (primario), Cancelar reserva (outline error), Historial (tonal). Items, acompañantes y pagos en tarjetas. Eliminar el botón inferior Volver y conservar `OnBackClicked`.

- [ ] **Step 2: Reestilizar filas dinámicas**

Ajustar filas de items y pasajeros a tarjetas y tokens; conservar datos.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 9: `AdminBookingQrPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminBookingQrPage.xaml`

- [ ] **Step 1: Reescribir XAML**

Header `Check-in` / `Código QR`, flecha, sin Salir. QR centrado en tarjeta; pasajero y viaje; tarjeta de asientos, estado y hora de check-in; botón Marcar como abordado primario. Eliminar Volver inferior. Conservar `QrImage`, `PassengerLabel`, `TripLabel`, `SeatsLabel`, `StateLabel`, `CheckedInAtLabel`, `BoardButton` y `OnBoardClicked`.

- [ ] **Step 2: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 10: `AdminTripBookingsPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripBookingsPage.xaml`

- [ ] **Step 1: Reescribir XAML**

Header `Operación` / `Reservas y pagos`, flecha y Salir. Buscador y filtro de estado en tarjetas. Tarjeta por reserva con cliente, asientos, total, badge de estado, saldo y botón Gestionar pagos. Conservar `SearchEntry`, `StatusPicker`, `Loading`, `BookingsRefresh`, `BookingsList`, `TripBookingItem` y eventos.

- [ ] **Step 2: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 11: `AdminTripOptionsPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripOptionsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripOptionsPage.xaml.cs`

- [ ] **Step 1: Reescribir XAML**

Header `Gestión de viajes` / `Opciones del viaje`, flecha y Salir. Resumen del viaje; formulario en tarjeta; lista de opciones como tarjetas con badge Activa/Inactiva y acciones. Conservar `OptionsLayout`, `BenefitsEditor`, `ActiveSwitch`, `EmptyLabel`, `LoadingIndicator` y eventos.

- [ ] **Step 2: Reestilizar filas dinámicas**

Mantener datos y acciones actuales; badges y botones de 44×44.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 12: `AdminTripMapPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripMapPage.xaml`

- [ ] **Step 1: Reescribir XAML**

Header `Operación` / `Mapa e itinerario`, flecha y Salir. Grid con header, título, buscador, `MapControl` y botones de modo. El mapa ocupa el espacio restante. Reemplazar `#00A85A`, `#1E88E5` y `#C62828` por `Primary`, `Secondary` y `Error`. Conservar `TripLabel`, `PlaceSearch`, `SearchButton`, `TripMap`, `ModeHint` y eventos.

- [ ] **Step 2: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 13: `AdminAuditPage`

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminAuditPage.xaml`

- [ ] **Step 1: Reescribir XAML**

Header `Historial` / `Historial de cambios`, flecha y Salir. Lista de auditoría en tarjetas con resumen, autor/fecha y diff; estado vacío centrado. Eliminar Volver inferior. Conservar `AuditRefresh`, `AuditTitle`, `AuditList`, `EmptyLabel` y `OnRefreshing`.

- [ ] **Step 2: Compilar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

---

### Task 14: Validación final

**Files:** ninguno nuevo.

- [ ] **Step 1: Verificar que no quedan barras de Shell ni colores antiguos**

Run: `rg 'ToolbarItem' src/TravelAgency.App/Modules/Admin/Views`
Expected: sin resultados.

Run: `rg '#1976D2|#2B6CB0|#E2E8F0|#1A202C|#F2F4F7|#B00020|#C53030|#FFF3CD|#7A5C00|#00A85A|#1E88E5|#C62828' src/TravelAgency.App/Modules/Admin`
Expected: solo casos justificados.

- [ ] **Step 2: Compilar y probar**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errores.

Run: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj`
Expected: 18/18 pruebas aprobadas.

- [ ] **Step 3: Validación visual**

El usuario revisa manualmente las 13 pantallas en la app.

- [ ] **Step 4: Commit y push**

Solo con confirmación explícita del usuario en ese momento.
