#!/bin/sh
# Starts an XFCE desktop on display :1 with TigerVNC, noVNC on port 6080, and the app at $APP_PATH when it exists.
set -eu

GEOMETRY="${GEOMETRY:-1600x1000}"

mkdir -p "$HOME/.vnc"
cat > "$HOME/.vnc/xstartup" <<'EOF'
#!/bin/sh
unset SESSION_MANAGER DBUS_SESSION_BUS_ADDRESS
exec dbus-launch --exit-with-session startxfce4
EOF
chmod +x "$HOME/.vnc/xstartup"

# No VNC password: the ports are meant to be published to 127.0.0.1 only.
vncserver :1 -geometry "$GEOMETRY" -depth 24 -localhost no -SecurityTypes None --I-KNOW-THIS-IS-INSECURE \
    -xstartup "$HOME/.vnc/xstartup"

websockify --web /usr/share/novnc 6080 localhost:5901 >/tmp/novnc.log 2>&1 &

if [ -x "$APP_PATH" ]; then
    # Give the desktop a moment, then start the app; its output goes to a log for checks.
    (sleep 3 && "$APP_PATH" >/tmp/app.log 2>&1) &
else
    echo "No app at $APP_PATH: the desktop runs without it." >&2
fi

echo "Desktop ready: noVNC on container port 6080, VNC on 5901"
# Keep the container running; the VNC log shows what happens on the desktop.
exec tail -F "$HOME/.vnc/"*.log
