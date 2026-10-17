#!/usr/bin/env bash
# Compila y lanza la app en MacCatalyst.
# Uso: build-run.sh [--local] [--no-build]
#
#   --local     apunta la app a la API local (TRAVELAGENCY_API_BASEURL) para poder
#               mutar datos sin tocar produccion. Requiere api-local.sh start.
#   --no-build  no compila, solo relanza lo ya compilado.
source "$(dirname "$0")/lib.sh"

LOCAL=0
DO_BUILD=1
for arg in "$@"; do
	case "$arg" in
		--local) LOCAL=1 ;;
		--no-build) DO_BUILD=0 ;;
		*) echo "argumento desconocido: $arg" >&2; exit 1 ;;
	esac
done

if [ "$DO_BUILD" = "1" ]; then
	echo "compilando (MacCatalyst)..."
	dotnet build "$REPO_ROOT/src/TravelAgency.App/TravelAgency.App.csproj" -f net10.0-maccatalyst -v q
fi

if [ ! -d "$APP_BUNDLE" ]; then
	echo "no existe el bundle: $APP_BUNDLE" >&2
	exit 1
fi

if app_running; then
	echo "cerrando instancia previa"
	pkill -x "$PROC" || true
	sleep 2
fi

if [ "$LOCAL" = "1" ]; then
	if ! lsof -nP -iTCP:"$API_PORT" -sTCP:LISTEN >/dev/null 2>&1; then
		echo "ADVERTENCIA: la API local no escucha en el puerto $API_PORT (usa api-local.sh start)" >&2
	fi
	echo "lanzando contra API local: $API_BASE_URL"
	open -n --env "TRAVELAGENCY_API_BASEURL=$API_BASE_URL" "$APP_BUNDLE"
else
	echo "lanzando contra produccion (BaseUrl por defecto)"
	open -n "$APP_BUNDLE"
fi

wait_window 30
echo "app lista (PID $(pgrep -x "$PROC" | head -1))"
