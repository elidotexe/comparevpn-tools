# Security policy

Please report vulnerabilities privately via GitHub's
[private vulnerability reporting](https://github.com/elidotexe/comparevpn-tools/security/advisories/new)
rather than opening a public issue. We aim to respond within 7 days.

## Privacy guarantees

These tools handle visitor IP addresses. By design:

- Lookups run locally against an on-disk dataset. No IP address is sent to a third-party API.
- IP addresses are never logged or stored by the app.
- HTTP responses are sent with `Cache-Control: no-store`.

A change that breaks any of these is treated as a security issue.
