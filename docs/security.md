# Security baseline

- Run as a standard user by default (`asInvoker`).
- Do not change system DNS, routes, firewall rules, or proxy settings without explicit consent.
- Never disable TLS certificate validation or install an interception root certificate.
- Keep diagnostics local; disclose any future telemetry before sending it.
- Any future VPN kill switch must be fail-closed and tested against tunnel failure, reboot, sleep/resume, IPv4, IPv6, and DNS leak scenarios before being advertised.
- Shared relays must be opt-in, bandwidth-capped, quota-limited, revocable, authenticated, and protected against abuse.
- Do not expose a user's PC as an unrestricted public exit node.
- Use established cryptographic libraries and audited protocol implementations; do not design custom cryptography.


## Encrypted DNS diagnostics

- DNS benchmark queries use DNS-over-HTTPS (DoH) over HTTPS/TLS only.
- Built-in public endpoints: Alibaba Cloud Public DNS (`https://dns.alidns.com/dns-query`), DNSPod Public DNS (`https://doh.pub/dns-query`), Cloudflare, Google Public DNS, and Quad9.
- Normal TLS certificate and hostname validation remains enabled; redirects are not followed automatically.
- If an encrypted endpoint fails, the diagnostic reports failure and does not fall back to plaintext UDP/53.
- The benchmark does not change Windows DNS configuration. Other applications may continue using the DNS settings already configured on the device.
- Resolving the DoH provider's own hostname may require the operating system's existing bootstrap resolver. The application does not claim to eliminate all DNS bootstrap leakage.
- The query hostname and DNS message are encrypted between the client and the DoH provider; the provider itself can still see the requested domain and the client's network address.
