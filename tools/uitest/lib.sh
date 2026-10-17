#!/usr/bin/env bash
# Utilidades compartidas del harness de pruebas UI para la app MAUI (MacCatalyst).
# Uso: source lib.sh

set -euo pipefail

UITEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$UITEST_DIR/../.." && pwd)"

PROC="${PROC:-TravelAgency.App}"
CONFIG="${CONFIG:-Debug}"
RID="maccatalyst-arm64"
APP_BUNDLE="${APP_BUNDLE:-$REPO_ROOT/src/TravelAgency.App/bin/$CONFIG/net10.0-maccatalyst/$RID/TravelAgency.App.app}"
APP_BIN="$APP_BUNDLE/Contents/MacOS/TravelAgency.App"
API_PORT="${API_PORT:-5199}"
API_BASE_URL="${API_BASE_URL:-http://127.0.0.1:$API_PORT}"
SHOTS_DIR="${SHOTS_DIR:-$UITEST_DIR/shots}"
API_LOG="${API_LOG:-$UITEST_DIR/api.log}"

# Biblioteca AppleScript.
#
# Trampas verificadas en este equipo (no "mejorar" sin volver a probar):
#  1. `entire contents of window` hay que asignarlo a una variable antes de recorrerlo;
#     usar `item n of (entire contents of ...)` en linea falla con -1700.
#  2. `value of attribute X` necesita contexto de System Events y acepta el nombre en
#     variable, pero NO compila con contexto de System Events en un handler suelto.
#  3. No se admite `return` dentro de un `if` de una sola linea (error -2741).
#  4. El nombre del proceso para System Events es el ejecutable: TravelAgency.App.
AX_LIB='
on attrOf(el, a)
	try
		tell application "System Events" to tell el to set v to (value of attribute a)
		if v is missing value then
			return ""
		end if
		return v as string
	on error
		return ""
	end try
end attrOf

on findEl(win, key, want)
	tell application "System Events"
		tell process "__PROC__"
			set ec to (entire contents of win)
			repeat with el in ec
				if my attrOf(el, key) = want then
					return el
				end if
			end repeat
		end tell
	end tell
	return missing value
end findEl

on winFrame()
	tell application "System Events" to tell process "__PROC__"
		set p to value of attribute "AXPosition" of window 1
		set s to value of attribute "AXSize" of window 1
		return ((item 1 of p) as string) & " " & ((item 2 of p) as string) & " " & ((item 1 of s) as string) & " " & ((item 2 of s) as string)
	end tell
end winFrame
'
AX_LIB="${AX_LIB//__PROC__/$PROC}"

# ax '<codigo applescript>' -> ejecuta el bloque con la biblioteca cargada.
# El marcador __PROC__ se sustituye por el nombre del proceso.
ax() {
	local main="${1//__PROC__/$PROC}"
	osascript -e "$AX_LIB" -e "$main"
}

# Escapa una cadena para incrustarla en un literal AppleScript.
as_escape() {
	printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# Resuelve un elemento por AutomationId (--id) o por texto visible (--text).
# Imprime el bloque AppleScript que deja la referencia en la variable "target".
# Falla (exit 1) si no encuentra el elemento.
ax_target() {
	local by="$1" want="$2"
	local key
	case "$by" in
		id)   key="AXIdentifier" ;;
		text) key="AXDescription" ;;
		*) echo "ax_target: selector invalido '$by'" >&2; return 1 ;;
	esac
	printf 'set target to my findEl(w, "%s", "%s")\n' "$key" "$want"
	printf 'if target is missing value then\n'
	printf '  error "no se encontro el elemento %s=%s"\n' "$by" "$want"
	printf 'end if\n'
}

# Parsea "--id <AutomationId>" o "--text <texto visible>" y deja SEL_BY / SEL_WANT.
parse_sel() {
	SEL_BY=""
	SEL_WANT=""
	while [ $# -gt 0 ]; do
		case "$1" in
			--id)
				SEL_BY="id"
				SEL_WANT="${2:-}"
				shift 2
				;;
			--text)
				SEL_BY="text"
				SEL_WANT="${2:-}"
				shift 2
				;;
			*)
				echo "selector invalido: $1 (usa --id o --text)" >&2
				return 1
				;;
		esac
	done
	if [ -z "$SEL_BY" ] || [ -z "$SEL_WANT" ]; then
		echo "falta el selector: --id <AutomationId> o --text <texto visible>" >&2
		return 1
	fi
}

app_running() {
	pgrep -x "$PROC" >/dev/null 2>&1
}

wait_window() {
	local tries="${1:-30}"
	local i
	for i in $(seq 1 "$tries"); do
		if ax 'tell application "System Events" to tell process "'"$PROC"'" to return count of windows' >/dev/null 2>&1; then
			if [ "$(ax 'tell application "System Events" to tell process "'"$PROC"'" to return count of windows' 2>/dev/null)" != "0" ]; then
				return 0
			fi
		fi
		sleep 1
	done
	echo "wait_window: la ventana no aparecio en ${tries}s" >&2
	return 1
}

activate_app() {
	ax 'tell application "System Events" to tell process "'"$PROC"'" to set frontmost to true' >/dev/null
}
