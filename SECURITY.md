# Security policy

## Supported versions

Security fixes are shipped for the current stable OPS Monitor release. Update
to the latest release before reporting a defect.

## Reporting a vulnerability

Do not open a public issue for a vulnerability. Use GitHub's private
**Security advisories → Report a vulnerability** flow for this repository:

https://github.com/seNkoKG/ops-monitor/security/advisories/new

Include the affected version, Windows version, reproduction steps, expected
impact, and any proof-of-concept files. Please avoid including personal sensor
data or local paths that identify you.

OPS Monitor stores telemetry locally, does not require an account, and does
not transmit analytics. Weather requests go directly to the documented public
weather providers. The optional elevated sensor broker publishes only a
bounded per-user snapshot in its protected installation data directory.

Release ZIPs include SHA-256 checksum files. Production code signing can be
enabled by maintainers through `native/Build.ps1 -CertificateThumbprint` when
a trusted code-signing certificate is available.
