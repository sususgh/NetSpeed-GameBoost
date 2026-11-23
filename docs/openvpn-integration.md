# OpenVPN profile support

NetSpeed-GameBoost can hand a user-supplied .ovpn profile to the official OpenVPN Community openvpn.exe on Windows.

## Current integration

- The VPN card offers OpenVPN or WireGuard.
- OpenVPN mode detects the standard OpenVPN Community executable locations.
- The user selects a provider-supplied .ovpn profile.
- A bounded structural validation checks for a remote host/port and flags risky script/plugin/management directives.
- Starting OpenVPN requests Windows administrator approval because the official client may need to create/configure a TUN adapter.
- Selecting OpenVPN again while this application has an active OpenVPN process offers to stop that process.

## Important limitations

- The application starts/stops the OpenVPN process; it does not yet consume OpenVPN's management interface or parse the log to verify TLS negotiation, server push, assigned address, handshake, default routes, DNS, or IPv6 leak status.
- A live process is not a verified connected VPN. UI text explicitly warns about this.
- No public VPN node list, subscription, credentials, or .ovpn profiles are bundled. Users must obtain profiles from a trusted VPN provider or operate their own server.
- This integration is a global tunnel client when the provider profile pushes/sets full-tunnel routes. It is not per-game split tunneling and does not by itself implement game UDP relay selection.
- Some legitimate provider profiles contain scripts, plugins, or management directives; this first guarded importer rejects those directives rather than executing arbitrary commands. Such profiles require a separately reviewed and trusted integration path.
- Credentials and private keys must remain in the provider's profile/credential mechanism. Never commit .ovpn, certificates, keys, usernames, or passwords to the repository.

## Manual validation required

1. Install the official OpenVPN Community Windows client from its official distribution.
2. Obtain an .ovpn profile from a trusted provider.
3. Import it using the VPN card and approve the UAC prompt.
4. Verify the OpenVPN log shows successful initialization and a completed TLS session.
5. Verify the tunnel adapter, routes, DNS behavior, IPv4/IPv6 leak behavior, and recovery after disconnect.
6. Test the intended game separately. This feature does not claim game-specific traffic steering.

Until automated management-interface and route verification are implemented, report only OpenVPN process started/running, never VPN connected or game accelerated.
