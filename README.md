# CompareVPN.ISPLookup

Who is your ISP? Find the internet provider, network (ASN), and country behind your IP address.
An open-source tool for the [CompareVPN](https://comparevpn.co) website, which you can inspect or self-host.

The solution contains one ASP.NET Core application, `CompareVPN.ISPLookup`.
The repository and solution keep the broader CompareVPN Tools name for future tools.

## Privacy

- ISP lookups run locally against the public-domain [iptoasn.com](https://iptoasn.com) dataset.
- IP addresses are never logged or stored, and lookup responses use `Cache-Control: no-store`.

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
./scripts/download-data.sh
dotnet run --project CompareVPN.ISPLookup
# open http://localhost:5180
```

Local requests show a private address such as `::1`. Deploy behind a public IP or configure
trusted reverse proxies to see the visitor's ISP.

In development the app reads `../data/ip2asn-combined.tsv.gz` (set in `appsettings.Development.json`).

## API

| Endpoint | Description |
|---|---|
| `GET /api/tools/isp` | The calling visitor's ISP |
| `GET /api/tools/isp/{ip}` | Any IPv4 / IPv6 address. Rate limited per client (`LookupRequestsPerMinute`, default 30) → `429` |

```json
{
  "ipAddress": "73.1.2.3",
  "ipVersion": 4,
  "isPublic": true,
  "network": {
    "asn": 7922,
    "name": "Comcast Xfinity",
    "organization": "COMCAST-7922 - Comcast Cable Communications, LLC",
    "countryCode": "US",
    "range": { "start": "73.0.0.0", "end": "73.255.255.255" },
    "isHosting": false
  },
  "dataUpdatedAt": "2026-11-04T21:46:11+00:00"
}
```

- `name` is a cleaned, human-friendly name; `organization` is the raw dataset description.
- `countryCode` is where the network is **registered**, not the visitor's location.
- `isHosting` is true for known cloud / datacenter / VPN-provider networks (`Tools/IspChecker/HostingNetworks.cs`).
  It is conservative: `false` does not prove an address is residential.
- `network` is `null` for private addresses (`isPublic: false`) and for public addresses not in the dataset.
- Invalid input returns `400`, a missing dataset returns `503`.

## Configuration

All settings live under `Tools:IspChecker` and can be set with environment variables
(`Tools__IspChecker__<Name>`). List settings take one comma-separated value
(`Tools__IspChecker__TrustedProxies=172.18.0.0/16, 10.0.0.5`) or indexed entries (`__0`, `__1`, …).
Invalid values stop the app at startup with a message naming the bad entry.

| Setting | Default | Description |
|---|---|---|
| `DataFilePath` | `data/ip2asn-combined.tsv.gz` | Dataset path; relative paths resolve against the content root. Use an absolute path in production. |
| `TrustedProxies` | *(empty: loopback only)* | Reverse proxies allowed to set `X-Forwarded-For`, as IPs or CIDRs, e.g. `172.18.0.0/16`. Replaces the loopback default when set. |
| `AllowedOrigins` | *(empty: CORS off)* | Sites whose browser JavaScript may call the API, e.g. `https://comparevpn.co`. Supports `https://*.example.com` and `*`. |
| `ReloadCheckInterval` | `00:01:00` | How often to check the dataset file for changes. `00:00:00` disables reloading. |
| `MaxDataAge` | `3.00:00:00` | Older data makes `/healthz` report `Degraded`. |
| `LookupRequestsPerMinute` | `30` | Per-client limit for `GET /api/tools/isp/{ip}`. |

## Calling the API from a browser

To call the API from JavaScript on another site, allow that site's origin:

```bash
Tools__IspChecker__AllowedOrigins=https://comparevpn.co, https://*.comparevpn.co
```

```js
const res = await fetch('https://isp-api.example.com/api/tools/isp');
if (res.ok) {
  const { ipAddress, network } = await res.json();   // network is null for private / unknown IPs
} else {
  const problem = await res.json();                    // 400 / 429 / 503 with { detail }
}
```

Notes:

- CORS applies to the API endpoints only (not `/healthz`) and allows `GET` only, with no cookies or
  credentials. Requests need no special headers, so browsers send them without a preflight.
- `429` and error responses carry the CORS headers too, so the page can show a proper message.
- An origin is scheme + host (+ port): `https://comparevpn.co`, not `https://comparevpn.co/tools`.
  Invalid entries stop the app at startup.
- Because the browser connects directly, the API sees the visitor's IP. If the API sits behind a
  reverse proxy or CDN, configure `TrustedProxies` as described below.

## Deployment

### Dataset

The dataset is not part of the publish output. Keep it outside the app directory (in Docker, a
volume) and point `DataFilePath` at it:

```bash
# Once, and then daily (e.g. cron). The file is replaced atomically.
scripts/download-data.sh /var/lib/comparevpn-tools
Tools__IspChecker__DataFilePath=/var/lib/comparevpn-tools/ip2asn-combined.tsv.gz
```

**No restart is needed after a refresh.** The app checks the file's modification time every
`ReloadCheckInterval`, loads a changed file in the background and swaps it in once it has fully
loaded. Lookups are never interrupted, and a missing, empty or corrupt replacement is logged and
ignored, so the previous data keeps serving. The check polls rather than relying on file-system
events, so it also works on Docker Desktop bind mounts, NFS and Kubernetes volumes.

If the file is missing at startup the app still starts, logs a warning with the path it tried,
returns `503` from the API and picks the file up as soon as it appears.

Memory: about 200 MB with the dataset loaded, peaking around 300 MB while a reload holds both
copies. A container limit of 384 MB leaves enough headroom.

### Health check

`GET /healthz` returns:

| Status | HTTP | Meaning |
|---|---|---|
| `Healthy` | 200 | Dataset loaded and fresh |
| `Degraded` | 200 | Dataset loaded but older than `MaxDataAge`: the refresh job is probably failing |
| `Unhealthy` | 503 | Dataset not loaded yet; lookups would return `503` |

Use it as a readiness check, or as a Docker `HEALTHCHECK`.

### Reverse proxies

The app reads the visitor's IP from `X-Forwarded-For`, but only when the request comes from a
trusted proxy. Behind nginx, Traefik or Caddy in Docker, trust the Docker network:

```bash
Tools__IspChecker__TrustedProxies=172.18.0.0/16
```

Without this, every visitor is reported with the proxy's private address. With it, the app walks
back through the trusted hops to the first untrusted address, so clients can't spoof their IP by
sending their own `X-Forwarded-For`. If Cloudflare (or another CDN) sits in front, its IP ranges
must be trusted too. A server that calls this API on behalf of visitors (server-side rendering)
counts as a proxy: add its address and have it forward the visitor's IP in `X-Forwarded-For`.

## Repository layout

```
CompareVPN.ISPLookup/               Web application (the only project in the solution)
  Tools/IspChecker/                 ISP lookup implementation and HTTP integration
  wwwroot/index.html                ISP lookup page
CompareVPN.Tools.IspChecker.Tests/  Standalone test project
scripts/                           Dataset download helper (optional target directory argument)
data/                              Local ASN dataset
```

Keep ISP lookup features in this application. Future tools can be added to the repository
when needed; separate libraries are unnecessary unless code needs to be distributed independently.

## Development

```bash
dotnet build
dotnet test CompareVPN.Tools.IspChecker.Tests/CompareVPN.Tools.IspChecker.Tests.csproj
```

The tests are kept outside the application solution so it contains only the runnable project.
CI runs the test project explicitly. Release tags such as `v0.1.0` build an application publish
artifact instead of NuGet packages.

## License

[MIT](LICENSE). The iptoasn.com dataset is released under the PDDL (public domain).
