# Galería multi-imagen por viaje — Diseño

**Fecha:** 2026-10-24
**Estado:** aprobado en conversación, pendiente de revisión del spec escrito
**Ámbito:** app .NET MAUI (cliente y admin) + API ASP.NET Core
**Fuera de ámbito:** recreación React, pasarela de pagos, cambios al flujo de reserva

---

## 1. Contexto

Hoy cada viaje tiene una sola imagen en `Trip.ImageUrl`. El cliente la ve en las tarjetas
de Explorar, Favoritos, Mis Viajes, Detalle del viaje y Detalle de reserva. El admin sube
una sola imagen desde `AdminDashboardPage` con `MediaPicker.PickPhotoAsync` y
`POST /api/trips/{id}/image`.

Se requiere:

- Varias imágenes por viaje.
- Carrusel de imágenes en el detalle del viaje y en el detalle de la reserva.
- Visor de pantalla completa con zoom y pan al tocar una imagen.
- Alta/edición de viaje con selección múltiple, reordenamiento, portada y eliminación.

---

## 2. Objetivos

1. Permitir hasta 10 imágenes por viaje.
2. Mantener una portada para tarjetas y listados.
3. Mostrar un carrusel en las dos pantallas de cliente donde la imagen es protagonista.
4. Permitir abrir una imagen en pantalla completa y hacer zoom.
5. Permitir al admin administrar la galería al crear o editar un viaje.
6. No romper la app actual ni los clientes que solo conocen `Trip.ImageUrl`.
7. Migrar los viajes existentes sin intervención manual.

## 3. No objetivos

- Carrusel dentro de las tarjetas de Home/Favoritos/Mis Viajes.
- Swipe entre imágenes dentro del visor de pantalla completa.
- Edición de imágenes (recorte, filtros, rotación manual).
- Drag-and-drop para reordenar; se usan flechas.
- Soporte de video.
- Cambios al flujo de reserva, pagos o check-in.
- Actualizar la especificación de recreación React en este cambio.

---

## 4. Decisiones de diseño

| Decisión | Elección |
|---|---|
| Almacenamiento | Tabla `TripImages` |
| Compatibilidad | `Trip.ImageUrl` se conserva como espejo de la portada |
| Máximo | 10 imágenes por viaje |
| Portada | Exactamente una si hay imágenes |
| Orden | `Order` contiguo 1..N |
| Carrusel cliente | Detalle del viaje y detalle de la reserva |
| Tarjetas | Siguen usando `Trip.ImageUrl` |
| Visor | Una sola imagen, con zoom y pan |
| Admin | Agregar, eliminar, reordenar con flechas y elegir portada |
| Borrado de viaje | No se toca en este cambio |

---

## 5. Modelo de datos

### 5.1 Entidad `TripImage`

```text
TripImage
- Id          int PK
- TripId      int FK Trips (Cascade)
- Url         varchar(500) NOT NULL
- Order       int NOT NULL
- IsCover     bool NOT NULL
- CreatedAt   timestamp NOT NULL
```

`Trip.Images` es una colección ordenada por `Order`.

`TripImage.Trip` existe como navegación pero se marca `[JsonIgnore]` para evitar ciclos.

### 5.2 Invariantes

- `0 <= Images.Count <= 10`.
- Si `Images.Count > 0`, exactamente una imagen tiene `IsCover = true`.
- `Order` es contiguo y empieza en 1.
- `Trip.ImageUrl` es igual a la URL de la imagen con `IsCover = true`.
- Si no hay imágenes, `Trip.ImageUrl` puede ser null o el placeholder existente.
- No se crea una fila `TripImage` para el placeholder automático de un viaje nuevo.

### 5.3 Límite compartido

`TripImageRules.MaxPerTrip = 10` en `TravelAgency.Shared`, para que API y app usen el mismo valor.

---

## 6. API

### 6.1 Endpoints nuevos

#### `POST /api/trips/{id}/images`

- Auth: `AdminOnly`.
- Content-Type: `multipart/form-data`.
- Campo: `files` (1..N archivos).
- Valida:
  - Viaje existe.
  - `currentCount + files.Count <= 10`.
  - Cada archivo tiene extensión `.jpg`, `.jpeg`, `.png` o `.webp`.
  - Cada archivo pesa <= 20 MB.
  - Cada archivo se procesa con `TripImageProcessor`.
- Procesa y sube las imágenes.
- Inserta filas con `Order` continuando el máximo actual.
- Si ya había imágenes sin portada, se normaliza antes de insertar.
- Si no había portada, la primera imagen subida se marca como portada y actualiza
  `Trip.ImageUrl`.
- Si una imagen falla, revierte las subidas ya hechas en esa petición y devuelve `400`.
- Respuesta:

```json
{
  "trip": { "...": "..." },
  "uploaded": [
    { "id": 21, "url": "...", "order": 4, "isCover": false }
  ]
}
```

`uploaded` respeta el orden de los archivos enviados.

#### `DELETE /api/trips/{id}/images/{imageId}`

- Auth: `AdminOnly`.
- Valida que la imagen pertenezca al viaje.
- Elimina la fila y el archivo del storage.
- Si era portada, elige la imagen con menor `Order` como nueva portada.
- Si no quedan imágenes, `Trip.ImageUrl = null`.
- Devuelve el `Trip` actualizado.

#### `PUT /api/trips/{id}/images/order`

- Auth: `AdminOnly`.
- Body:

```json
{ "imageIds": [3, 1, 2] }
```

- Valida que el conjunto de IDs sea exactamente el de las imágenes del viaje.
- Asigna `Order = 1..N` en el orden recibido.
- No cambia la portada.
- Devuelve el `Trip` actualizado.

#### `PUT /api/trips/{id}/images/{imageId}/cover`

- Auth: `AdminOnly`.
- Valida que la imagen pertenezca al viaje.
- Pone `IsCover = false` en todas y `true` en la elegida.
- Actualiza `Trip.ImageUrl` a la URL de la portada.
- Devuelve el `Trip` actualizado.

### 6.2 Endpoint existente `POST /api/trips/{id}/image`

Se conserva por compatibilidad.

Comportamiento nuevo:

- Si existe una portada, sube la nueva imagen, actualiza la URL de esa fila y borra el
  archivo anterior.
- Si hay filas pero ninguna portada, usa la de menor `Order`, la marca como portada y
  actualiza su URL.
- Si no hay filas, crea una fila `Order=1`, `IsCover=true`.
- Siempre actualiza `Trip.ImageUrl`.

No crea una imagen paralela en la galería.

### 6.3 Respuestas con imágenes

- `GET /api/trips/{id}` incluye `Images` ordenadas.
- `GET /api/trips/manage` incluye `Images` para edición.
- `GET /api/bookings/{id}` incluye `Trip.Images` para el detalle de reserva.
- `GET /api/trips` sigue devolviendo `ImageUrl`; no incluye la galería.
- `GET /api/favorites` sigue devolviendo `ImageUrl`; no incluye la galería.
- `GET /api/users/{id}/bookings` sigue usando `Trip.ImageUrl` para tarjetas.

### 6.4 Errores

| Caso | Mensaje |
|---|---|
| Más de 10 | “Un viaje puede tener máximo 10 imágenes.” |
| Sin archivos | “Selecciona al menos una imagen.” |
| Formato inválido | “Formato no permitido. Usa JPG, PNG o WebP.” |
| Archivo > 20 MB | “La imagen no puede superar los 20 MB.” |
| Imagen no válida | “El archivo no es una imagen válida.” |
| Imagen de otro viaje | “La imagen no pertenece a este viaje.” |
| Orden inválido | “El orden debe incluir exactamente las imágenes del viaje.” |

---

## 7. App cliente

### 7.1 `TripImageCarousel`

Nuevo `ContentView` reutilizable.

Propiedades:

- `ItemsSource`: lista de `TripImageItem`.
- `HeightRequest`: 230 en detalle de viaje, 200 en detalle de reserva.
- `ImageTapped`: evento con URL e índice.

Comportamiento:

- `CarouselView` con `Loop="False"`.
- `IsSwipeEnabled` solo si hay más de una imagen.
- `IndicatorView` visible solo si hay más de una imagen.
- Contador flotante “1 / N” si hay más de una.
- Cada slide usa `NetworkImage` con `Aspect="AspectFill"`.
- `TapGestureRecognizer` abre el visor.
- Si no hay imágenes, muestra el placeholder y no abre el visor.

### 7.2 `TripImageItem`

```text
Url          string?
Source       ImageSource
IsCover      bool
```

Construcción por viaje:

1. Ordenar `Trip.Images` por `Order`.
2. Crear un `TripImageItem` por imagen.
3. Si no hay filas y existe `Trip.ImageUrl`, agregar esa portada como único item.
4. Si no hay nada, un item con `Source = placeholder`.

### 7.3 Integración

- `ClientTripDetailPage`: reemplaza el `NetworkImage` del hero por `TripImageCarousel`.
- `ClientMyBookingDetailPage`: reemplaza el `NetworkImage` del hero por `TripImageCarousel`.
- Home, Favoritos y Mis Viajes no cambian.

### 7.4 Visor `ClientImageViewerPage`

Ruta: `imageviewer?url=&title=`.

UI:

- Fondo negro.
- Botón cerrar.
- Título del viaje y contador si aplica.
- Imagen `Aspect="AspectFit"` con `NetworkImage`.
- Indicador de carga mientras descarga.

Gestos:

- Pinch: zoom de 1x a 4x.
- Pan: solo con zoom > 1.
- Doble toque: alterna 1x y 2.5x.
- Reset al abrir y al cambiar de orientación.
- Sin swipe entre imágenes.

Resolución:

- Carrusel: `LargeImageWidth = 800`.
- Visor: `FullImageWidth = 1600`.

### 7.5 Accesibilidad

- `SemanticProperties.Description` en cada slide: “Foto N de M”.
- Botón cerrar con `SemanticProperties.Description = "Cerrar visor"`.
- El visor respeta el botón atrás del sistema.
- No depender solo del color para indicar portada o posición.

---

## 8. App admin

### 8.1 Pantalla

En `AdminDashboardPage`, la tarjeta “Imagen del viaje” se reemplaza por
“Imágenes del viaje”:

- Contador `N / 10`.
- Botón `📷 Agregar imágenes`.
- `CollectionView` horizontal con miniaturas.
- Cada miniatura:
  - Badge `Portada`.
  - Botones `←`, `→`, `Portada`, `Eliminar`.
- Empty state: “Sin imágenes. Se usará el placeholder.”
- Texto de ayuda: “La portada se muestra en las tarjetas. Usa las flechas para ordenar.”

No hay drag-and-drop.

### 8.2 `AdminTripImageItem`

```text
Id?          int
Url?         string
File?        FileResult
Source       ImageSource
Order        int
IsCover      bool
IsNew        bool
IsDeleted    bool
```

### 8.3 Selección múltiple

```csharp
MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
{
    Title = "Selecciona imágenes del viaje",
    SelectionLimit = TripImageRules.MaxPerTrip - currentCount,
    MaximumWidth = 1920,
    MaximumHeight = 1920,
    CompressionQuality = 80,
    RotateImage = true
});
```

Validaciones en cliente:

- Formatos `.jpg`, `.jpeg`, `.png`, `.webp`.
- Máximo 20 MB por archivo.
- No superar 10 imágenes.
- Mostrar nombre y tamaño.
- Si un archivo no es válido, avisar y no agregarlo.

### 8.4 Flujo de guardado

1. Validar y guardar el viaje (`POST` o `PUT`).
2. Si es creación, asignar `_editingTrip = created` antes de tocar imágenes.
3. Subir imágenes nuevas con `POST /images`.
4. Reordenar con `PUT /images/order`.
5. Marcar portada si cambió.
6. Eliminar imágenes existentes marcadas para borrar.
7. Recargar el viaje y navegar a `//admintrips`.

El orden minimiza pérdida de datos:

- Si falla la subida, no se borró nada.
- Si falla el reorden o la portada, las imágenes siguen existiendo.
- Las eliminaciones van al final.
- Si el viaje se creó pero falló la subida, queda en modo edición para reintentar sin duplicar.

### 8.5 Métodos de `ApiService`

- `UploadTripImagesAsync(int tripId, IReadOnlyList<FileResult> files)`
- `DeleteTripImageAsync(int tripId, int imageId)`
- `ReorderTripImagesAsync(int tripId, IReadOnlyList<int> imageIds)`
- `SetTripImageCoverAsync(int tripId, int imageId)`

`UploadTripImageAsync` actual se conserva para compatibilidad.

---

## 9. Migración y compatibilidad

### 9.1 `EnsureTripImagesTableAsync`

- Crea `TripImages` en SQLite y Postgres.
- FK `TripId -> Trips` con `Cascade`.
- Índice por `TripId`.
- No se crea índice único `(TripId, Order)` para permitir reordenamientos intermedios.

### 9.2 Backfill

- Para cada viaje con `ImageUrl` no nulo y sin filas en `TripImages`:
  - Insertar una fila `Order=1`, `IsCover=true`.
- Se omiten URLs que terminen en `/placeholder.jpg`.
- El backfill es idempotente.

### 9.3 Orden de arranque

`EnsureTripImagesTableAsync` se llama antes de `EnsureDefaultTripOptionsBackfillAsync`.

### 9.4 Compatibilidad

- La app vieja sigue usando `Trip.ImageUrl`.
- `POST /api/trips/{id}/image` sigue funcionando.
- La app nueva usa `Images` y cae a `ImageUrl` si la API aún no tiene la tabla.

### 9.5 Despliegue

1. Desplegar la API.
2. Verificar la tabla y el backfill.
3. Publicar la app.
4. Si la app se publica antes, el carrusel funciona con `ImageUrl`, pero el admin
   multi-imagen falla hasta actualizar la API.

---

## 10. Pruebas

### 10.1 Unitarias

- Límite de 10.
- Cálculo del siguiente `Order`.
- Reasignación de portada al eliminar.
- Fallback de `TripImageItem`.
- Reordenamiento de `AdminTripImageItem`.

### 10.2 Integración API

- Subir 3 imágenes y recibir IDs/orden.
- Rechazar la imagen 11.
- Rechazar formato y tamaño inválidos.
- Eliminar portada y reasignar.
- Reordenar con el conjunto exacto de IDs.
- Cambiar portada y verificar `Trip.ImageUrl`.
- `POST /image` legacy reemplaza la portada.
- Solo Admin puede mutar; Coordinador recibe 403.
- `GET /api/trips/{id}` y `GET /api/bookings/{id}` incluyen imágenes.

### 10.3 Manual

- Carrusel con 0, 1 y N imágenes.
- Swipe, puntos y contador.
- Abrir visor, cerrar, pinch, pan, doble toque y reset.
- Admin: selección múltiple, preview, reorden, portada, eliminar, crear y editar.
- Android, iOS y MacCatalyst.

### 10.4 Build

```bash
dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst
dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj
dotnet test
```

---

## 11. Riesgos

| Riesgo | Mitigación |
|---|---|
| Gesto horizontal del carrusel choca con el scroll | Probar en las tres plataformas; ajustar `IsSwipeEnabled`/`PeekAreaInsets` si hace falta. |
| Memoria por 10 imágenes | Hero 800 px, visor 1600 px, tarjetas 400 px. |
| HEIC de iPhone | Se mantienen los formatos actuales; si el picker entrega HEIC se rechaza con mensaje. |
| Falla a mitad de subida | El endpoint revierte lo subido en esa petición. |
| Archivos huérfanos | Borrar storage después de confirmar la fila; limpiar si falla la transacción. |
| Portada duplicada o inexistente | Invariante server-side y normalización al modificar. |
| Migración en producción | `Ensure*` idempotente; desplegar API primero. |
| Duplicar viaje al reintentar creación | Pasar a modo edición después del `POST` exitoso. |

---

## 12. Criterios de aceptación

1. Un viaje puede tener 0..10 imágenes.
2. La portada se muestra en las tarjetas y listados existentes.
3. El detalle del viaje y el detalle de la reserva muestran carrusel.
4. Tocar una imagen abre el visor con zoom y pan.
5. El admin puede agregar, eliminar, reordenar y elegir portada.
6. Los viajes existentes con una imagen se migran automáticamente.
7. La app vieja sigue funcionando con `Trip.ImageUrl`.
8. El API y la app compilan; los tests pasan.
9. No se rompe el flujo de reserva ni el check-in.
10. No se commitean secretos ni se borran archivos ajenos al cambio.

---

## 13. Archivos previstos

**Shared**
- `src/TravelAgency.Shared/Models/TripImage.cs`
- `src/TravelAgency.Shared/Models/TripImagesUploadResult.cs`
- `src/TravelAgency.Shared/Models/Trip.cs` (colección `Images`)

**API**
- `src/TravelAgency.Api/Data/AppDbContext.cs`
- `src/TravelAgency.Api/Program.cs` (endpoints + `EnsureTripImagesTableAsync`)

**App**
- `src/TravelAgency.App/Controls/TripImageCarousel.xaml` / `.xaml.cs`
- `src/TravelAgency.App/ViewModels/TripImageItem.cs`
- `src/TravelAgency.App/Modules/Admin/ViewModels/AdminTripImageItem.cs`
- `src/TravelAgency.App/Modules/Client/Views/ClientImageViewerPage.xaml` / `.xaml.cs`
- `src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml` / `.xaml.cs`
- `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml` / `.xaml.cs`
- `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml` / `.xaml.cs`
- `src/TravelAgency.App/Services/ApiService.cs`
- `src/TravelAgency.App/AppShell.xaml.cs`
- `src/TravelAgency.App/MauiProgram.cs`
- `src/TravelAgency.App/Resources/Styles/Styles.xaml` (si hace falta)

---

## 14. Notas de implementación

- No cambiar el flujo de reserva.
- No tocar `POST /api/trips/{id}/transport`.
- No borrar `Trip.ImageUrl` de la base.
- No usar `HttpClient` sin `FileResult.OpenReadAsync()`.
- No cargar las imágenes completas en listados; usar `GetTripImage` con ancho.
- No hacer commit ni push sin confirmación explícita.
