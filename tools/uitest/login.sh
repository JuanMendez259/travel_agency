#!/usr/bin/env bash
# Inicia sesion con uno de los perfiles de prueba.
# Uso: login.sh [cliente|admin|coordinador]
#
# Rellena correo y contrasena por accesibilidad y pulsa "Entrar", luego espera
# a que aparezca la barra de pestanas (AutomationId "Explorar") para confirmar.
source "$(dirname "$0")/lib.sh"

PROFILE="${1:-cliente}"

case "$PROFILE" in
	cliente)
		EMAIL="cliente@travelagency.com"
		PASS="Cliente123!"
		;;
	admin)
		EMAIL="admin@travelagency.com"
		PASS="Admin123!"
		;;
	coordinador)
		EMAIL="coordinador@gmail.com"
		PASS="agencia2026"
		;;
	*)
		echo "perfil invalido: $PROFILE (usa cliente, admin o coordinador)" >&2
		exit 1
		;;
esac

if ! app_running; then
	echo "la app no esta en ejecucion; usa build-run.sh primero" >&2
	exit 1
fi

SCRIPT="$(cat <<'AS'
tell application "System Events"
	tell process "__PROC__"
		set frontmost to true
		delay 1
		set w to window 1
		set fEmail to my findEl(w, "AXIdentifier", "LoginEmail")
		if fEmail is missing value then
			if my findEl(w, "AXIdentifier", "Explorar") is not missing value then
				return "ya-autenticado"
			end if
			return "sin-formulario-de-login"
		end if
		set fPass to my findEl(w, "AXIdentifier", "LoginPassword")
		set fBtn to my findEl(w, "AXIdentifier", "LoginSubmit")
		if fPass is missing value then
			return "sin-campo-de-contrasena"
		end if
		if fBtn is missing value then
			return "sin-boton-de-entrar"
		end if
		tell application "System Events" to tell fEmail to set value of attribute "AXValue" to "__EMAIL__"
		tell application "System Events" to tell fPass to set value of attribute "AXValue" to "__PASS__"
		delay 1
		click fBtn
		repeat 25 times
			delay 1
			if my findEl(window 1, "AXIdentifier", "Explorar") is not missing value then
				return "ok"
			end if
		end repeat
		return "timeout: no aparecio la barra de navegacion"
	end tell
end tell
AS
)"

SCRIPT="${SCRIPT//__EMAIL__/$(as_escape "$EMAIL")}"
SCRIPT="${SCRIPT//__PASS__/$(as_escape "$PASS")}"

RESULT="$(ax "$SCRIPT")"
echo "login ($PROFILE): $RESULT"

case "$RESULT" in
	ok|ya-autenticado) exit 0 ;;
	*) exit 1 ;;
esac
