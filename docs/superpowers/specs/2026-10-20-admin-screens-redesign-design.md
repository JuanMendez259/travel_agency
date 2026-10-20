# Rediseño de las pantallas de administración (Stitch → MAUI)

Fecha: 2026-10-20
Estado: aprobado para planificación

## 1. Contexto

Se rediseñaron en Stitch las cinco pantallas de administración y el usuario aprobó
cada una por su captura. Los mocks viven en `stitch/admin/` (ignorados por git) y son
la referencia visual:

| Pantalla | Mock | Página MAUI |
| --- | --- | --- |
| Viajes | `AdminTripsPage.html` | `AdminTripsPage.xaml` |
| Detalle del viaje | `AdminTripDetailPage.html` | `AdminTripDetailPage.xaml` |
| Check-in / Salida | `AdminCheckinPage.html` | `AdminCheckinPage.xaml` |
| Escanear QR | `AdminScanQrPage.html` | `AdminScanQrPage.xaml` |
| Mapa de asientos | `AdminTripSeatsPage.html` | `AdminTripSeatsPage.xaml` |

`AdminTripSeatsPage` ya usa parcialmente el lenguaje visual nuevo (cabecera verde,
leyenda, selector de piso), pero su XAML se reescribe para calcar el mock aprobado.
Las otras cuatro conservan el diseño anterior.

## 2. Objetivo y criterios de éxito

Reproducir en la app el diseño aprobado de las cinco pantallas, sin cambiar la lógica
de negocio ni los contratos de la API.

Se considera terminado cuando:

1. Las cinco pantallas muestran la cabecera, las tarjetas, los chips y los botones del
   mock, con los tokens de color y la tipografía del proyecto.
2. Todas las acciones y datos que hoy existen siguen funcionando y visibles según rol.
3. `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
   termina sin errores.
4. El usuario valida visualmente las pantallas en la app.

## 3. Decisiones aprobadas

1. **Alcance:** las cinco pantallas, incluida una reescritura visual del Mapa de
   asientos.
2. **Cabecera propia:** se oculta la barra de Shell y cada página dibuja su cabecera.
3. **Funciones extra:** opiniones, solicitudes de cupo, unidad de transporte y
   auditoría se conservan y se estilizan; no se elimina nada.
4. **Enfoque híbrido:** un solo componente compartido (`AdminPageHeader`); tarjetas,
   chips y filas se escriben inline en cada página.

## 4. Arquitectura

### 4.1 `AdminPageHeader`

Nuevo `Controls/Admin/AdminPageHeader.xaml` y `.xaml.cs` (`ContentView`).

Propiedades enlazables:

- `Eyebrow` (string): texto pequeño en mayúsculas, color `Primary`.
- `PageTitle` (string): título grande, `OnSurface`.
- `ShowBack` (bool, por defecto `false`): muestra la flecha ←.
- `ShowLogout` (bool, por defecto `true`): muestra el botón tonal Salir.

Eventos: `BackRequested`, `LogoutRequested`. Las páginas siguen manejando el cierre de
sesión con sus métodos actuales; el header solo dispara el evento.

Comportamiento de la flecha: `Shell.Current.GoToAsync("..")`; si no hay pila de
navegación, navega a la pestaña `//admintrips`.

Cada página declara `Shell.NavBarIsVisible="False"`, fondo `Surface` y
`SafeAreaEdges="Container"` para respetar notch y barras del sistema.

### 4.2 Estilos compartidos

Claves nuevas en `Resources/Styles/Styles.xaml`:

- `AdminCard`: `Border` sin trazo, `RoundRectangle 16`, fondo
  `SurfaceContainerLowest`, padding 16, sombra suave (opacidad 0.05, radio 10,
  desplazamiento 0,2).
- `AdminCardTitle`: `Label` 15-16, negrita, `OnSurface`.
- `AdminTonalButton`: fondo `SurfaceContainerHigh`, texto `OnSurface`.
- `AdminOutlineButton`: fondo transparente, borde `OutlineVariant`, texto `Primary`.
- `AdminChip`: botón compacto, radio 16, altura mínima 44, padding 14,6.
- `AdminBadge`: etiqueta compacta con fondo tonal y texto 11-12 en negrita.

Los estilos existentes de `Button` y `Border` siguen siendo la base; estas claves solo
los especializan. No se añaden colores nuevos: se usan los tokens de `Colors.xaml`
(`Primary`, `PrimaryContainer`, `Secondary`, `SecondaryContainer`, `Surface*`,
`OnSurface*`, `Outline`, `Error`). Las únicas excepciones son los estados del mapa de
asientos, ya existentes (`#DBB92A` ocupado).

### 4.3 Navegación y rol

No cambian las rutas de `AppShell.xaml.cs` ni la autenticación. Se conserva
`_session.IsAdmin` / `_session.IsCoordinator` y toda la lógica de visibilidad actual.
El header no conoce roles: cada página decide `ShowLogout`.

## 5. Diseño por pantalla

### 5.1 Viajes

- Header: eyebrow “Admin · Viajes”, título “Mis Viajes”, sin flecha, con Salir.
- Buscador dentro de una tarjeta con emoji 🔍 y `Entry` actual (`TripsSearchEntry`).
- Chips Todos / Activos / Pausados con el estilo `AdminChip` y el estado activo en
  `Primary` con texto blanco.
- Tarjeta por viaje: miniatura (`NetworkImage` si hay `Thumb`; si no, recuadro tonal
  con emoji), título, badge PAUSADO, destino, horario, transporte y precio.
- Fila de contadores: vendidos, disponibles y capacidad.
- Acciones: “Ver detalle” (tonal) y “Mapa de asientos” (primario), más
  Pausar/Reactivar al pie, visible solo si `CanManage`.
- Se conservan `RefreshView`, `TripsList`, `Loading`, `EmptyView`, los eventos
  `OnDetailClicked`, `OnSeatsClicked`, `OnToggleActiveClicked`,
  `OnTripsSearchTextChanged` y `OnTripsStatusChipClicked`.

### 5.2 Detalle del viaje

- Header: eyebrow “Gestión de viajes”, título “Detalle del viaje”, flecha ← y Salir.
- Hero card: imagen del viaje, tipo de transporte con emoji, título, destino, fechas,
  transporte, precio adulto y niño, chips de estado, cancelación y cupo.
- Tarjeta de métricas de tres columnas: Vendidos, Disponibles, Capacidad.
- Nota de cancelación en tarjeta mint (`SecondaryContainer` suave) con el texto de
  `CancellationDaysLimit`.
- Descripción en tarjeta.
- “Hotel y opciones”: resumen de hotel, resumen de opciones, filas de opción con
  nombre, precios y badge Activa, y botón “Administrar opciones” (tonal).
- “Solicitudes de cupo”: se conserva `CapacityRequestsLayout` con filas estilizadas.
- “Pasajeros”: avatar con iniciales, nombre, asientos y total, badge de estado y
  acciones Llamar / WhatsApp cuando hay teléfono. Se mantienen el resumen “+N reservas
  más”, el total y `PassengersTotalLabel`.
- “Operación”: cinco filas con icono y chevron — Reservas y pagos, Mapa e itinerario,
  Check-in / Salida, Finalizar viaje e Historial de cambios.
- “Administración”: Editar, Pausar y Eliminar dentro de `EditActions`, solo admin.
- “Información de la unidad”: imagen, placas, estado vacío y editor actuales, dentro de
  tarjeta.
- “Opiniones de clientes”: promedio, lista y estado vacío actuales, estilizados.
- Cierre: “Volver a mis viajes”.

Todos los `x:Name` usados por `AdminTripDetailPage.xaml.cs` se conservan.

### 5.3 Check-in / Salida

- Header: eyebrow “Gestión de viajes”, título “Check-in / Salida”, flecha ← y Salir.
- Datos del viaje: título, destino y fechas.
- Tarjeta de estado con el texto de `StatusLabel`, la hora de salida cuando exista y
  tres pills: pasajeros abordados, asientos y pendientes.
- Acciones con la visibilidad actual: Escanear QR (primario), Abrir/Cerrar check-in y
  Marcar salida / Reabrir salida.
- “Pasajeros”: encabezado con conteo y filas con avatar, nombre, rol, asientos, total
  y hora de abordaje, badge de estado, interruptor y botón QR.
  - Rol: el primer pasajero de cada reserva se muestra como “Titular de la reserva” y
    el resto como “Acompañante”. Es una derivación de presentación, no un dato nuevo.
- Nota informativa: los acompañantes se controlan por separado y cada uno tiene su QR.
- Estado vacío actual (`NoPassengersLabel`) estilizado.
- Se conservan `OnScanClicked`, `OnOpenCheckinClicked`, `OnCloseCheckinClicked`,
  `OnCompleteClicked`, `OnReopenClicked`, `OnBoardToggledAsync`,
  `OnPassengerBoardToggledAsync`, `BuildPassengerRow`, `BuildTripPassengerRow` y
  `OnBackClicked`.

### 5.4 Escanear QR

- Header: eyebrow “Check-in”, título “Escanear QR”, flecha ←, sin Salir.
- Tarjeta de contexto: emoji 🧭, título del viaje y destino.
- Área de cámara con `ScannerView` y marco de cuatro esquinas; badge “Cámara activa” y
  mensaje “Coloca el código dentro del marco”. Se conservan los estados de permiso
  denegado y el botón de reintento actuales.
- Panel de resultado: `ResultLabel` y `DetailLabel`.
- “Verificación manual” en tarjeta: `ManualTokenEntry`, botón Verificar y texto de
  ayuda.
- Nota de seguridad: cada código registra un pasajero; si ya había abordado se avisa.
- Pie: “Solo personal autorizado · Los escaneos quedan registrados en el viaje”.
- Se conservan `OnBarcodesDetected`, `ProcessTokenAsync`, `OnManualVerifyClicked`,
  `OnResumeClicked`, `EnsureCameraPermissionAsync` y `LoadTripAsync`.

### 5.5 Mapa de asientos

- Header: eyebrow “Operación”, título “Mapa de asientos”, flecha ← y Salir.
- Tarjeta del viaje con emoji 🚌, título, tipo de transporte y resumen
  “N ocupados · M libres”; segunda línea con el cupo por piso cuando hay dos pisos.
- Leyenda Libre / Ocupado / Check-in.
- Tarjeta “Distribución de asientos” con badge de planta, selector Piso 1 / Piso 2,
  cabina, etiquetas Fila izq. / ↕ / Fila der., mapa y referencias de maletero y
  sanitario.
- “Detalle de ocupación” con el conteo y una fila por asiento ocupado: estado, nombre,
  piso, asiento y badge Check-in hecho / falta check-in.
- Se conserva la lógica de `LoadSeatsAsync`, `RenderMap`, `RenderMapRows`, `BuildSeat`,
  `FloorOf`, `SelectFloor`, `UpdateFloorButtons`, `OnFloor1Clicked` y `OnFloor2Clicked`.
- No se muestra folio de reserva: `Booking` no expone un código legible.

## 6. Datos y reglas

- Todo se alimenta de `Trip`, `Booking`, `TripPassenger`, `TripSeat` y
  `TripSeatMap`. No se añaden campos, endpoints ni migraciones.
- No se inventan datos. Si un dato no existe o viene vacío, el elemento se oculta:
  - Hora de abordaje: `CheckedInAt`; si falta, no se muestra.
  - Teléfono: solo si viene informado; sin él se muestra “Sin teléfono registrado”
    como en el detalle actual.
  - Precio de niño: solo si `ChildPrice` tiene valor; si es `null`, se indica que el
    niño paga tarifa de adulto.
  - QR por pasajero: solo si `QrToken` existe.
- Las fórmulas de totales, reembolsos y cupo siguen en `BookingRefundPolicy` y la API.

## 7. Estados

- Carga: `ActivityIndicator` centrado, como hoy.
- Vacío: texto honesto por sección; nunca datos de ejemplo.
- Error: mensaje devuelto por el servicio, sin bloquear el resto de la página.
- `RefreshView` se mantiene en Viajes y Mapa de asientos.

## 8. Accesibilidad

- Títulos de sección con `SemanticProperties.HeadingLevel`.
- Botones que solo muestran un emoji llevan `SemanticProperties.Description`.
- Áreas táctiles de al menos 44×44.
- Contraste: texto `OnSurface` sobre `SurfaceContainerLowest`; texto blanco solo sobre
  `Primary`.

## 9. Fuera de alcance

- Cambios en la API, en modelos compartidos o en migraciones.
- Pantallas de cliente.
- Pruebas de UI automatizadas.
- Commit o push sin confirmación explícita.

## 10. Validación

1. Compilar MacCatalyst con el comando de los criterios de éxito.
2. Revisar que no queden grises sueltos (`Gray200`, `#EEEEEE`, `#F4F4F4`) ni hex ajenos
   a los tokens en las cinco páginas.
3. Revisión visual del usuario en la app.

## 11. Riesgos

- **Gesto de volver en iOS:** al ocultar la barra de Shell se pierde el swipe-back
  nativo; la flecha del header lo cubre.
- **Nombres de controles:** el XAML se reescribe, pero los `x:Name` que usa el
  code-behind deben conservarse para no romper la compilación.
- **Páginas largas:** el detalle y el check-in crecen; se mantienen en `ScrollView`.
- **Safe area:** MacCatalyst, Android e iOS difieren; se valida en MacCatalyst y se
  confía en `SafeAreaEdges` para el resto.

## 12. Criterios de aceptación

- [ ] Las cinco pantallas tienen cabecera propia con eyebrow, título y acciones del mock.
- [ ] Viajes muestra buscador, chips y tarjetas con contadores y acciones.
- [ ] Detalle conserva todas las secciones actuales, incluidas las extra.
- [ ] Check-in conserva la lógica de apertura, cierre, salida y abordaje.
- [ ] QR conserva cámara, permiso, verificación manual y reanudación.
- [ ] Asientos conserva pisos, estados y detalle de ocupación.
- [ ] Ningún dato inventado; lo ausente se oculta.
- [ ] La compilación MacCatalyst termina sin errores.
