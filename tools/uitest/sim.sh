#!/usr/bin/env bash
# Pruebas en el simulador de iOS. Es la superficie REAL de "pantalla grande":
# el iPad es produccion (iOS), MacCatalyst no.
#
# Uso:
#   sim.sh list                 lista dispositivos disponibles
#   sim.sh boot [device]        arranca y abre el simulador
#   sim.sh build                compila para iossimulator-arm64
#   sim.sh install [device]     instala la app en el simulador
#   sim.sh launch [device]      lanza la app
#   sim.sh run [device]         build + install + launch
#   sim.sh shot [nombre]        captura la pantalla del simulador
#   sim.sh tap <x> <y>          clic por coordenadas sobre la ventana del Simulator
#   sim.sh rotate               rota el simulador (forzar cambio de orientacion)
#
# Variable DEVICE_NAME para elegir dispositivo (por defecto un iPad).
source "$(dirname "$0")/lib.sh"

DEVICE_NAME="${DEVICE_NAME:-iPad Air 11-inch (M4)}"
BUNDLE_ID="${BUNDLE_ID:-com.companyname.travelagency.app}"
IOS_RID="iossimulator-arm64"
IOS_APP="$REPO_ROOT/src/TravelAgency.App/bin/$CONFIG/net10.0-ios/$IOS_RID/TravelAgency.App.app"

udid_of() {
	xcrun simctl list devices available | grep -F "$1 (" | head -1 | sed -E 's/.*\(([-A-F0-9]{36})\).*/\1/'
}

require_udid() {
	local u
	u="$(DEVICE_NAME="$DEVICE_NAME" udid_of "$DEVICE_NAME")"
	if [ -z "$u" ]; then
		echo "no encontre el dispositivo '$DEVICE_NAME' (usa: sim.sh list)" >&2
		exit 1
	fi
	printf '%s' "$u"
}

case "${1:-}" in
	list)
		xcrun simctl list devices available | grep -E "iPhone|iPad"
		;;
	boot)
		[ -n "${2:-}" ] && DEVICE_NAME="$2"
		UDID="$(require_udid)"
		echo "arrancando $DEVICE_NAME ($UDID)"
		xcrun simctl boot "$UDID" 2>/dev/null || true
		open -a Simulator
		xcrun simctl bootstatus "$UDID" -b >/dev/null 2>&1 || true
		echo "listo"
		;;
	build)
		echo "compilando net10.0-ios ($IOS_RID)..."
		dotnet build "$REPO_ROOT/src/TravelAgency.App/TravelAgency.App.csproj" \
			-f net10.0-ios -p:RuntimeIdentifier="$IOS_RID" -v q
		;;
	install)
		[ -n "${2:-}" ] && DEVICE_NAME="$2"
		UDID="$(require_udid)"
		if [ ! -d "$IOS_APP" ]; then
			echo "no existe $IOS_APP; ejecuta sim.sh build" >&2
			exit 1
		fi
		xcrun simctl install "$UDID" "$IOS_APP"
		echo "instalada en $DEVICE_NAME"
		;;
	launch)
		[ -n "${2:-}" ] && DEVICE_NAME="$2"
		UDID="$(require_udid)"
		xcrun simctl launch "$UDID" "$BUNDLE_ID"
		;;
	run)
		[ -n "${2:-}" ] && DEVICE_NAME="$2"
		"$UITEST_DIR/sim.sh" build
		"$UITEST_DIR/sim.sh" boot "$DEVICE_NAME"
		"$UITEST_DIR/sim.sh" install "$DEVICE_NAME"
		"$UITEST_DIR/sim.sh" launch "$DEVICE_NAME"
		;;
	shot)
		UDID="$(require_udid)"
		NAME="${2:-sim-$(date +%H%M%S)}"
		mkdir -p "$SHOTS_DIR"
		xcrun simctl io "$UDID" screenshot "$SHOTS_DIR/$NAME.png" >/dev/null 2>&1
		echo "$SHOTS_DIR/$NAME.png"
		;;
	tap)
		X="$2"
		Y="$3"
		osascript -e "tell application \"System Events\" to tell process \"Simulator\" to click at {$X, $Y}"
		;;
	rotate)
		UDID="$(require_udid)"
		osascript -e 'tell application "Simulator" to activate' >/dev/null 2>&1 || true
		osascript -e 'tell application "System Events" to keystroke (ASCII character 29) using command down'
		echo "rotacion solicitada (cmd+derecha)"
		;;
	*)
		sed -n '2,20p' "$0"
		exit 1
		;;
esac
