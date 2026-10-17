#!/usr/bin/env bash
# Escribe un valor en un campo de la ventana de la app.
# Uso: type.sh --id <AutomationId> --value "<texto>"
source "$(dirname "$0")/lib.sh"

VALUE=""
ARGS=()
while [ $# -gt 0 ]; do
	case "$1" in
		--value)
			VALUE="${2:-}"
			shift 2
			;;
		*)
			ARGS+=("$1")
			shift
			;;
	esac
done

if [ -z "$VALUE" ]; then
	echo "falta --value \"<texto>\"" >&2
	exit 1
fi

parse_sel "${ARGS[@]}"
TARGET_BLOCK="$(ax_target "$SEL_BY" "$SEL_WANT")"
ESCAPED="$(as_escape "$VALUE")"

ax "$(cat <<AS
set theValue to "$ESCAPED"
tell application "System Events"
	tell process "__PROC__"
		set w to window 1
$TARGET_BLOCK
		tell application "System Events" to tell target to set value of attribute "AXValue" to theValue
	end tell
end tell
return "type ok: $SEL_BY=$SEL_WANT"
AS
)"
