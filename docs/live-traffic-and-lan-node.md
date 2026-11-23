# Live traffic chart and local LAN node

## Live traffic flow
The dashboard samples Windows network-interface byte counters once per configured timer tick (default one second). It calculates receive/send rates from counter deltas and plots the latest 60 samples as separate download and upload lines. These are host-interface totals, not per-phone or per-game throughput; interface counters may include background traffic and can double-count traffic on some virtual adapter setups.

## Local LAN node endpoint
The configurable LAN node is a small read-only HTTP health/discovery endpoint:
- Default port: 8765; valid range: 1024–65535.
- The listener binds to all local interfaces so a phone connected to the PC's Windows Mobile Hotspot or the same LAN can reach the PC's LAN IPv4 address.
- GET / returns a simple status page.
- GET /health returns JSON health information.
- GET /metrics returns request count and configured port.
- The UI discovers active non-loopback IPv4 addresses, starts/stops the listener, reports bind errors (including port conflicts), and copies a URL for phone testing.
- No firewall rule is changed automatically. Windows Firewall may need to allow the app/port on a Private network profile.

Important: this is a LAN node discovery and health endpoint, **not** a SOCKS/HTTP proxy, VPN tunnel, UDP relay, or game-traffic forwarder. It proves that the phone can reach the PC's local listener, not that game traffic is accelerated. Do not expose the port to public networks.

## Manual phone test
1. Start the Windows Mobile Hotspot or connect PC and phone to the same private Wi-Fi.
2. Start the LAN node in NetSpeed and note the PC IPv4 address and port.
3. On the phone, open http://PC-LAN-IP:PORT/health.
4. A JSON response with status=ok confirms local reachability. If it times out, check that phone and PC are on the same LAN, Windows network profile is Private, and firewall allows the app on that private network.

## Limitations
The chart shows real host network-interface rates. It does not yet separate traffic by Android device, process, or game. The LAN node endpoint does not forward payload traffic. A real game relay still needs an authenticated proxy/tunnel protocol, UDP support, a phone-side client/per-app routing, and failure/leak protection tests.
