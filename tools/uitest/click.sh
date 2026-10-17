#!/usr/bin/env bash
# Pulsa un elemento de la ventana de la app.
# Uso: click.sh --id <AutomationId> | --text "<texto visible>"
source "$(dirname "$0")/lib.sh"

parse_sel "$@"
TARGET_BLOCK="$(ax_target "$SEL_BY" "$SEL_WANT")"

ax "$(cat <<AS
tell application "System Events"
	tell process "__PROC__"
		set w to window 1
$TARGET_BLOCK
		click target
	end tell
end tell
return "click ok: $SEL_BY=$SEL_WANT"
AS
)"
