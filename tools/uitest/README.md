# Harness de pruebas UI (MacCatalyst)

Automatiza la app MAUI en macOS con herramientas nativas (`open` + `osascript` +
`screencapture`), sin dependencias externas (ni Appium, ni cliclick). Sirve para
validar de verdad lo que los tests unitarios no ven: layout, estados vacios,
pantallas grandes, flujos completos.

Todos los scripts usan `AutomationId` de MAUI, que MacCatalyst expone como
`AXIdentifier`.

## Requisitos

- macOS con Xcode y permiso de **Accesibilidad** para Terminal (o el emisor) sobre
  `System Events`. La primera vez macOS lo pide al ejecutar `osascript`.
- App compilada para `net10.0-maccatalyst` (`build-run.sh` lo hace).

## Uso rapido

```bash
./api-local.sh start              # API local (SQLite, :5199) - opcional
./build-run.sh --local            # compila y lanza la app contra la API local
./login.sh cliente                # inicia sesion (cliente|admin|coordinador)
./dump.sh Login                   # lista el arbol AX filtrando por "Login"
./click.sh --text "Ver Detalles"  # pulsa por texto visible
./type.sh --id LoginEmail --value "x@y.com"
./resize.sh 1180 880              # prueba de pantalla grande
./shot.sh mi-captura              # captura la ventana en shots/
./api-local.sh stop               # para la API local
```

## Scripts

| Script | Que hace |
| --- | --- |
| `lib.sh` | Utilidades compartidas y biblioteca AppleScript (`source`). |
| `build-run.sh [--local] [--no-build]` | Compila y (re)lanza la app. `--local` la apunta a la API local. |
| `api-local.sh <start\|stop\|status\|log>` | API local con SQLite en el puerto 5199. |
| `dump.sh [filtro]` | Vuelca rol, AutomationId, texto y valor de cada elemento. |
| `click.sh --id <id> \| --text "<texto>"` | Pulsa un elemento. |
| `type.sh --id <id> --value "<texto>"` | Escribe en un campo. |
| `login.sh [perfil]` | Login por accesibilidad; espera la barra de pestanas. |
| `resize.sh <w> <h> [x] [y]` | Redimensiona/reposiciona la ventana. |
| `shot.sh [nombre]` | Captura la ventana (no la pantalla entera). |

## Perfiles de prueba

| Perfil | Correo | Contrasena |
| --- | --- | --- |
| `cliente` | cliente@travelagency.com | Cliente123! |
| `admin` | admin@travelagency.com | Admin123! |
| `coordinador` | coordinador@gmail.com | agencia2026 |

## Modo local (seguro)

`build-run.sh --local` pasa `TRAVELAGENCY_API_BASEURL=http://127.0.0.1:5199` a la
app mediante `open --env`. `MauiProgram.cs` lee esa variable y la asigna a
`ApiService.BaseUrl`; solo si falta se usa produccion. Asi las pruebas pueden
crear y cancelar reservas sin tocar los datos reales.

Verificacion de que la app apunta a local:

```bash
grep "Request starting" api.log
```

## Trampas de AppleScript ya resueltas

No "mejorar" estos patrones sin volver a probarlos, cada uno costo una sesion:

1. `entire contents of window` debe asignarse a una variable antes de recorrerlo.
   `item n of (entire contents of ...)` en linea falla con el error `-1700`.
2. `value of attribute X` necesita contexto de `System Events` y acepta el nombre
   del atributo en variable. Fuera de ese contexto es error de sintaxis `-2741`.
3. No se admite `return` dentro de un `if ... then` de una sola linea (`-2741`).
4. El proceso para System Events se llama como el ejecutable: `TravelAgency.App`.
5. `AXPosition`/`AXSize` son listas: coercer a texto las concatena sin separador;
   extraer con `item 1 of p` / `item 2 of p`.
6. En capturas, `screencapture -R` usa las coordenadas de la ventana; si la ventana
   se mueve entre medir y capturar, el script reintenta una vez.

## Notas

- Sin `AutomationId`, los elementos se localizan por texto (`--text`), que es el
  `Text` del XAML y llega como `AXDescription`. Es menos estable.
- Los tabs de Shell ya exponen `AXIdentifier`: `Explorar`, `Mis Viajes`,
  `Mi Perfil`, `Nosotros`, `Avisos`.
- `shots/` y `api.log` estan ignorados por git.
