#!/usr/bin/env bash
# Captura la ventana de la app.
# Uso: shot.sh [nombre]
source "$(dirname "$0")/lib.sh"

NAME="${1:-captura-$(date +%H%M%S)}"
mkdir -p "$SHOTS_DIR"

FRAME="$(ax 'return my winFrame()')"
RECT="$(printf '%s\n' "$FRAME" | awk '{printf "%d,%d,%d,%d", $1, $2, $3, $4}')"
OUT="$SHOTS_DIR/$NAME.png"

echo "ventana (x y w h): $FRAME"
echo "rect: $RECT"

if ! screencapture -x -o -R"$RECT" "$OUT" 2>/dev/null; then
	# La ventana pudo moverse entre la medida y la captura: se reintenta una vez.
	sleep 1
	FRAME="$(ax 'return my winFrame()')"
	RECT="$(printf '%s\n' "$FRAME" | awk '{printf "%d,%d,%d,%d", $1, $2, $3, $4}')"
	screencapture -x -o -R"$RECT" "$OUT"
fi

if [ ! -s "$OUT" ]; then
	echo "shot: no se pudo capturar la ventana" >&2
	exit 1
fi

echo "$OUT"
