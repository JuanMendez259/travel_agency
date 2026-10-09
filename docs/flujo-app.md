# Diagrama de flujo — TravelAgency

Flujo de la app por roles: **Cliente**, **Coordinador** y **Admin**.

> Este bloque Mermaid se puede pegar directo en cualquier visor Markdown y también
> importar en Excalidraw (menú **More tools → Mermaid to Excalidraw**).

```mermaid
flowchart TD
    Start([Inicio de sesion]) --> Auth{Autenticacion}
    Auth -->|Credenciales validas| Role{Rol del usuario}
    Auth -->|Credenciales invalidas| Start

    Role -->|Cliente| Cliente
    Role -->|Coordinador| Coord
    Role -->|Admin| Admin

    subgraph Cliente["👤 Cliente"]
        direction TB
        C1[Explorar<br/>viajes activos · busqueda · categorias · favoritos]
        C2[Detalle del viaje<br/>favorito · compartir · opciones · asientos]
        C3{Hay cupo?}
        C4[Solicitar mas cupo<br/>aviso a staff]
        C5[Elegir asientos y pasajeros<br/>necesidades especiales · cupon de descuento]
        C6[Confirmar reserva]
        C7[Mis Viajes<br/>lista de reservas]
        C8[Detalle de reserva<br/>QR · pagos · acompanantes · mapa]
        C9[Calificar viaje]
        C10[Cancelar reserva<br/>segun politica de reembolso]
        C11[Perfil · Nosotros · Avisos]

        C1 --> C2 --> C3
        C3 -->|No| C4
        C3 -->|Si| C5 --> C6 --> C7 --> C8
        C8 --> C9
        C8 --> C10
    end

    subgraph Coord["🧑‍💼 Coordinador"]
        direction TB
        CO1[Lista de viajes<br/>activos y pausados]
        CO2[Detalle del viaje<br/>solo lectura]
        CO3[Finalizar / Reabrir viaje]
        CO4[Avisos<br/>reservas · cancelaciones · cupo]

        CO1 --> CO2 --> CO3
    end

    subgraph Admin["🛠️ Admin"]
        direction TB
        A1[Formulario de viaje<br/>crear · editar · pausar/reactivar · eliminar]
        A2[Reservas<br/>detalle · registrar pagos · cancelar · QR]
        A3[Opciones del viaje · Mapa de asientos · Mapa e itinerario]
        A4[Check-in / Salida]
        A5[Estadisticas]
        A6[Coordinadores]
        A7[Descuentos]
        A8[Auditoria · Avisos]
    end

    Cliente --> API
    Coord --> API
    Admin --> API

    API[(API .NET<br/>Supabase / Postgres)]

    C6 -. notifica nueva reserva .-> CO4
    C4 -. notifica solicitud de cupo .-> CO4
    A1 -. pausa/reactiva y avisa .-> C11
    CO3 -. finaliza y avisa .-> C11
```

## Notas de roles

| Rol | Puede | No puede |
| --- | --- | --- |
| **Cliente** | Explorar, reservar, pagar/abonar, cancelar (con politica), calificar, favoritos, perfil, avisos | Administrar viajes, ver otros clientes, gestionar descuentos |
| **Coordinador** | Ver viajes y su detalle, finalizar/reabrir, recibir avisos | Crear/editar/eliminar viajes, registrar pagos, check-in, auditoria, descuentos, coordinadores |
| **Admin** | Todo: viajes (crear/editar/pausar/reactivar/eliminar), reservas y pagos, opciones, asientos, mapa, check-in, estadisticas, coordinadores, descuentos, auditoria | — |

## Flujos de notificacion (panel Avisos)

- **Cliente → staff:** nueva reserva, solicitud de cupo, necesidades especiales.
- **Staff → cliente:** pago recibido / reserva liquidada, viaje finalizado (calificar),
  cancelacion, viaje pausado o reactivado.

## Importar en Excalidraw

1. Copia el bloque Mermaid de arriba.
2. En Excalidraw: **More tools → Mermaid to Excalidraw**, pega y genera.
3. Alternativa: importa `docs/flujo-app.excalidraw` (archivo nativo).
