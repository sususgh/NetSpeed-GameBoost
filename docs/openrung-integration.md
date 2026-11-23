# OpenRung integration

NetSpeed GameBoost consumes the upstream OpenRung client engine and relay
directory; it does not maintain a separate, hand-written relay list.

- Upstream: https://github.com/openrung/openrung
- Client source pin: `client-v0.5.6` commit `6d7e375cc76ecc0803bb5f913a99b057cb0330b8`
- License: GPL-3.0-or-later (see upstream LICENSE)
- Windows client: `cmd/client`, built with upstream's documented
  `with_utls,with_external_windivert` tags.
- Discovery helper: calls `connectcore.Engine.RankedDirectory`, which uses
  OpenRung's signed broker directory, expiry validation, candidate filtering
  and latency ranking.
- VPN: the upstream client runs `connect --tun -headless`; stdin EOF is
  patched as a graceful stop request so the engine's deferred teardown runs.
  The patch is in `patches/openrung-headless-stdin.patch`.

The client is pinned and built from source during Windows CI. The executable
and discovery helper are bundled under `OpenRung/` in both the installer and
portable package. No relay IPs, credentials, or fabricated node entries are
embedded in this application.

The OpenRung directory is dynamic: an empty list means the broker currently
has no usable candidates for this client/network. A listed relay is a
broker-advertised candidate, not a guarantee of game-optimized latency.
