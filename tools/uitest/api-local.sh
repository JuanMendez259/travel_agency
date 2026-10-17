#!/usr/bin/env bash
# Arranca o para la API local (SQLite, puerto 5199) para las pruebas UI.
# Uso: api-local.sh <start|stop|status|log>
#
# Nunca toca produccion: usa Database__Provider=sqlite, que crea un archivo local.
source "$(dirname "$0")/lib.sh"

API_DLL="$REPO_ROOT/src/TravelAgency.Api/bin/$CONFIG/net10.0/TravelAgency.Api.dll"

api_pid() {
	lsof -nP -iTCP:"$API_PORT" -sTCP:LISTEN -t 2>/dev/null | head -1
}

case "${1:-}" in
	start)
		if [ -n "$(api_pid)" ]; then
			echo "la API ya escucha en el puerto $API_PORT (PID $(api_pid))"
			exit 0
		fi
		if [ ! -f "$API_DLL" ]; then
			echo "compilando la API..."
			dotnet build "$REPO_ROOT/src/TravelAgency.Api/TravelAgency.Api.csproj" -v q
		fi
		echo "arrancando API local en $API_BASE_URL (log: $API_LOG)"
		(
			cd "$UITEST_DIR" || exit 1
			Database__Provider=sqlite \
			ASPNETCORE_URLS="$API_BASE_URL" \
			nohup dotnet "$API_DLL" >"$API_LOG" 2>&1 &
		)
		for _ in $(seq 1 30); do
			if [ -n "$(api_pid)" ]; then
				echo "API lista (PID $(api_pid))"
				exit 0
			fi
			sleep 1
		done
		echo "la API no arranco; revisa $API_LOG" >&2
		exit 1
		;;
	stop)
		PID="$(api_pid)"
		if [ -z "$PID" ]; then
			echo "la API no esta corriendo"
			exit 0
		fi
		kill "$PID"
		echo "API detenida (PID $PID)"
		;;
	status)
		PID="$(api_pid)"
		if [ -z "$PID" ]; then
			echo "detenida"
		else
			echo "corriendo en $API_BASE_URL (PID $PID)"
		fi
		;;
	log)
		tail -n 60 "$API_LOG"
		;;
	*)
		echo "uso: api-local.sh <start|stop|status|log>" >&2
		exit 1
		;;
esac
