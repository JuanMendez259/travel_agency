#!/usr/bin/env bash
# Redimensiona y reposiciona la ventana de la app (pruebas de pantallas grandes).
# Uso: resize.sh <ancho> <alto> [x] [y]
source "$(dirname "$0")/lib.sh"

if [ $# -lt 2 ]; then
	echo "uso: resize.sh <ancho> <alto> [x] [y]" >&2
	exit 1
fi

W="$1"
H="$2"
X="${3:-}"
Y="${4:-}"

ax "$(cat <<AS
tell application "System Events"
	tell process "__PROC__"
		set position of window 1 to {60, 40}
		set size of window 1 to {$W, $H}
		delay 1
	end tell
end tell
return my winFrame()
AS
)"

if [ -n "$X" ] && [ -n "$Y" ]; then
	ax "$(cat <<AS
tell application "System Events" to tell process "__PROC__" to set position of window 1 to {$X, $Y}
return "posicion: $X,$Y"
AS
)"
fi
