# Windows PC hotspot relay for a phone

## Chosen product behavior

The Windows 10 PC is the phone's Wi-Fi access point and network relay. The phone joins the Windows Mobile Hotspot; the PC forwards traffic through its selected upstream Internet connection.

The desktop client exposes two explicit modes:

### 1. All phone traffic

- The phone connects to the Windows Mobile Hotspot.
- Windows Internet Connection Sharing (ICS / SharedAccess) performs the downstream NAT and forwarding.
- The upstream connection may be Ethernet or another active adapter with an IPv4 default gateway.
- The current client only inspects active adapters and the SharedAccess service, and opens ms-settings:network-mobilehotspot.
- The user must enable Mobile Hotspot and verify Windows' shared-connection selection in Windows Settings.
- A running SharedAccess service or a visible virtual adapter is not proof that ICS is correctly bound or that the phone has Internet access.

### 2. Game-only phone traffic

A Windows Mobile Hotspot cannot identify which Android application originated a forwarded packet. The PC sees forwarded phone traffic, not the Android process identity.

True per-game routing therefore requires:
- A phone-side VPN client that supports per-app routing (for example, selecting only the game in the VPN app).
- A valid WireGuard-compatible server/relay configuration.
- A server/relay path that forwards the selected game traffic to the Internet and returns it.
- DNS, IPv4/IPv6, MTU, reconnect and failure behavior tests.

The desktop UI currently documents this requirement and does not claim the mode is active. It does not install routes, alter ICS, change firewall rules, or start a tunnel automatically.

## Safety and validation boundary

PhoneHotspotRelayManager is read-only. It enumerates active adapters, identifies the Microsoft Wi-Fi Direct virtual adapter by its description, looks for active adapters with an IPv4 gateway, and queries the SharedAccess service state.

It intentionally does not:
- Enable or disable Mobile Hotspot.
- Change Internet Connection Sharing bindings.
- Modify routes, NAT, DNS or firewall rules.
- Start a VPN tunnel or create a kill switch.
- Claim successful phone connectivity based only on service state.

## Manual Windows 10 acceptance test

1. Connect the PC to the Internet using the intended upstream adapter.
2. Open Settings → Network & Internet → Mobile hotspot from the application.
3. Select the intended upstream connection and enable Mobile Hotspot.
4. Connect the Android phone to the hotspot SSID.
5. Verify that the phone receives an IP address, gateway and DNS server, then browse a test site.
6. Refresh the desktop diagnostics and compare the reported adapter/service state with Windows Settings.
7. For game-only testing, install/configure a phone-side per-app VPN and a real relay endpoint; verify that only the selected game uses the tunnel while unrelated phone apps use the normal hotspot path.

The automated Windows CI tests cover mode semantics and adapter classification only. They do not emulate a physical Wi-Fi adapter, Android phone, working ICS binding, or an external relay.
