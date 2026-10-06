# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

Una sola app .NET MAUI que se despliega en Android y iOS; MacCatalyst/Windows existen en el proyecto pero no son objetivo de producción.

## Users

Dos audiencias dentro de la misma app:

- **Viajero / cliente (usuario principal).** Personas hispanohablantes (contexto México) que buscan escapadas y excursiones. Descubren viajes, revisan disponibilidad y precio, reservan asientos por opción, registran pagos/anticipos, gestionan y cancelan su reserva, hacen check-in con QR, califican el viaje y guardan favoritos.
- **Staff de la agencia.** Administrador y Coordinador operan el back-office dentro de la misma app: alta/edición de viajes y opciones, cupo, reservas, check-in y escaneo de QR, coordinadores, auditoría y estadísticas.

## Product Purpose

Es la app móvil de una agencia de turismo de aventura/eco que cubre todo el ciclo del viaje: el cliente reserva un lugar y el staff lo opera. El éxito es una reserva completada sin fricción y un viaje controlado de punta a punta (cupo, check-in, cancelaciones), con la misma información y reglas para ambos lados.

## Positioning

Reserva por asiento y por opción (paquete) con cupo limitado por el transporte y, si hay hospedaje, también por el cupo del hotel. Un solo producto sirve al cliente y al staff, y las reglas de negocio críticas —como la política de reembolso— viven en una fuente única compartida entre API y app, en lugar de duplicarse.

## Operating Context

- Uso móvil nativo, en movilidad y frecuentemente a plena luz del día.
- Los viajes se hacen por opciones/paquetes: siempre existe una opción base obligatoria ("Entrada General") más variantes con precio, beneficios y capacidad propios (capacidad compartida o propia).
- Transporte por Camión o Camioneta con mapa de asientos; el viaje puede incluir hospedaje.
- Check-in con código QR (de la reserva y del usuario), escaneado por el staff.
- Cancelaciones sujetas a la política configurada por viaje.
- Mapas con puntos de interés del viaje.
- Back-office: viajes, opciones, cupo, reservas, coordinadores, auditoría y estadísticas.

## Capabilities and Constraints

- **Roles:** `Client`, `Admin`, `Coordinador`.
- **Estado de reserva:** `Pending`, `Confirmed`, `Cancelled`.
- **Tarifa de niño:** `ChildPrice` es opcional; si es `null`, el niño paga tarifa de adulto. Rango de niño: 0 a 11 años.
- **Cancelación:** dentro de `CancellationDaysLimit` del viaje (default 3) el reembolso es del 100% de lo abonado; fuera del límite hay multa del 30% y se devuelve el 70%. La fórmula vive en `BookingRefundPolicy` y la usan tanto la API como la app.
- **Cupo reservable:** si el viaje incluye hotel, el cupo efectivo es el mínimo entre la capacidad del transporte y el cupo del hotel.
- **Backend:** ASP.NET Core Minimal API; Supabase/Postgres en producción y SQLite como respaldo local. Al arrancar ejecuta migraciones idempotentes `Ensure*` que agregan columnas/tablas faltantes.
- **Idioma:** la UI es en español.
- **Decisión abierta — marca:** el nombre definitivo del negocio no está decidido.
- **Decisión abierta — pagos:** no se confirmó si existe una pasarela de pago real o si los pagos/anticipos se registran manualmente.

## Brand Commitments

No hay marca definitiva: "RutaVerde México" es un nombre de trabajo/placeholder. Los datos y acreditaciones que muestra la pantalla "Nosotros" (cifras de exploradores, rutas, reforestación, NOM-09, Leave No Trace, WFR, comercio justo) son contenido de muestra, no compromisos verificados, y no deben presentarse como reales.

## Evidence on Hand

- No hay testimonios, prensa, casos de estudio ni clientes verificados.
- Las imágenes de viajes provienen de cargas del administrador (campo `ImageUrl`).
- Las cifras y credenciales de la UI son placeholder (ver Brand Commitments).
- No fabricar métricas, acreditaciones ni datos de negocio.

## Product Principles

1. El viajero nunca debe dudar: disponibilidad, cupo y precio claros antes de reservar.
2. Una sola app, dos sombreros: cliente y staff comparten dominio y reglas; el back-office no duplica la lógica del cliente.
3. Las reglas de negocio tienen una sola fuente de verdad (p. ej. `BookingRefundPolicy`) compartida entre API y app.
4. Honestidad de datos: nunca inventar cifras, acreditaciones ni disponibilidad.
5. Operación móvil a prueba de sol y prisa: legible, táctil y en español.


## [IMPECCABLE CONTEXT OVERRIDE: .NET MAUI MOBILE APPLICATION]

### Target Platform & Engineering Constraints
- **Framework & Technology:** We are developing a native/multi-platform mobile application using **.NET MAUI (Multi-platform App UI)**.
- **UI Architecture:** All user interfaces are written in declarative **XAML (Extensible Application Markup Language)** or through **.NET MAUI ResourceDictionaries** for styling.
- **DO NOT GENERATE:** Web technologies. Never write or output HTML, CSS, Tailwind CSS utility classes, React code, or raw JavaScript.

### Mapping Impeccable Design Language to .NET MAUI
When applying Impeccable commands (such as `/shape`, `/polish`, `/typeset`, or `/layout`), translate Web/CSS principles to their strict .NET MAUI equivalents:
- **Spacing/Margins:** Translate CSS `padding` and `margin` into .NET MAUI `Padding` and `Margin` properties (e.g., `Thickness="10,20,10,20"`). Ensure a strict mathematical rhythm that fits mobile screens, avoiding cluttered views.
- **Typography:** Translate CSS `font-size`, `font-weight`, and line spacing into .NET MAUI `FontSize`, `FontAttributes="Bold"`, and `CharacterSpacing`. Avoid standard mobile font sizing slop.
- **Containers & Nesting:** Enforce Impeccable's anti-card rule. In .NET MAUI, do not wrap components into endless nested `Frame` or `Border` elements. Utilize efficient layouts like `Grid` and `VerticalStackLayout` to prevent visual nesting and layout performance hits.
- **Colors & Branding:** All colors must be mapped to .NET MAUI `Color` definitions, semantic AppColors, or stored dynamically inside `App.xaml` (`ResourceDictionary`). Avoid flat, boring grises; always apply subtle, tinted shades native to mobile screens (including dark mode optimization for OLED/Amoled displays).
- **Interactive States & Controls:** Map interaction changes into .NET MAUI `VisualStateManager` (VSM) states (e.g., Normal, PointerOver, Selected) instead of pseudo-classes like `:hover`.

### Mode of Operation
When interpreting commands like `/impeccable shape` or `/impeccable polish`, act as a Senior Mobile UX Engineer. Audit the XAML node structure, layout alignment, typography hierarchy, and platform-specific visual rhythm for Android and iOS devices.
