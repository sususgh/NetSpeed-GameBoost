# Security baseline

- Run as a standard user by default (`asInvoker`).
- Do not change system DNS, routes, firewall rules, or proxy settings without explicit consent.
- Never disable TLS certificate validation or install an interception root certificate.
- Keep diagnostics local; disclose any future telemetry before sending it.
- Any future VPN kill switch must be fail-closed and tested against tunnel failure, reboot, sleep/resume, IPv4, IPv6, and DNS leak scenarios before being advertised.
- Shared relays must be opt-in, bandwidth-capped, quota-limited, revocable, authenticated, and protected against abuse.
- Do not expose a user's PC as an unrestricted public exit node.
- Use established cryptographic libraries and audited protocol implementations; do not design custom cryptography.
