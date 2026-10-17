#!/usr/bin/env bash
# Vuelca el arbol de accesibilidad de la ventana de la app.
# Uso: dump.sh [filtro]
#   filtro  texto opcional: solo se muestran elementos cuyo rol, AutomationId,
#           texto visible o valor contengan el filtro (insensible a mayusculas).
source "$(dirname "$0")/lib.sh"

ax "$(cat <<'AS'
set out to ""
tell application "System Events"
	tell process "__PROC__"
		set ec to (entire contents of window 1)
		repeat with el in ec
			set r to my attrOf(el, "AXRole")
			set i to my attrOf(el, "AXIdentifier")
			set d to my attrOf(el, "AXDescription")
			set v to my attrOf(el, "AXValue")
			set out to out & r & " | id=[" & i & "] dsc=[" & d & "] val=[" & v & "]" & linefeed
		end repeat
	end tell
end tell
return out
AS
)" | { if [ -n "${1:-}" ]; then grep -i -- "$1"; else cat; fi; }
