# CompareVPN.ISPLookup

Who is your ISP? Find the internet provider, network (ASN), country and approximate location behind your IP
address.
An open-source tool for the [CompareVPN](https://comparevpn.co) website, which you can inspect or self-host.

The solution contains one ASP.NET Core application, `CompareVPN.ISPLookup`.
The repository and solution keep the broader CompareVPN Tools name for future tools.
Tools that run entirely in the visitor's browser, such as the WebRTC leak test, keep their code in `web/`
(see [Browser code](#browser-code-web)).

## Privacy

- ISP lookups run locally against the public-domain [iptoasn.com](https://iptoasn.com) dataset, and the
  optional location against the [DB-IP](https://db-ip.com) "IP to City Lite" file. No address is sent to
  another service.
- IP addresses are never logged or stored, and lookup responses use `Cache-Control: no-store`.
- The WebRTC leak test runs in the browser. Like every WebRTC test, the browser asks an address server
  (Cloudflare's public STUN server by default) for its public address, so that server sees it. See
  [Browser code](#browser-code-web).

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
./scripts/download-data.sh    # network list (~11 MB) and location file (~60 MB download)
dotnet run --project CompareVPN.ISPLookup
# open http://localhost:5180
```

To skip the location file, run `SKIP_LOCATION_DATA=1 ./scripts/download-data.sh`; lookups then answer with
`"location": null`.

Local requests show a private address such as `::1`. Deploy behind a public IP or configure
trusted reverse proxies to see the visitor's ISP.

In development the app reads `../data/ip2asn-combined.tsv.gz` and `../data/dbip-city-lite.mmdb` (set in
`appsettings.Development.json`).

## API

| Endpoint | Description |
|---|---|
| `GET /api/tools/isp` | The calling visitor's ISP and approximate location |
| `GET /api/tools/isp/{ip}` | The same for any IPv4 / IPv6 address. Rate limited per client (`LookupRequestsPerMinute`, default 30) → `429` |

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
  "dataUpdatedAt": "2026-11-04T21:46:11+00:00",
  "location": {
    "city": "Plant City",
    "region": "Florida",
    "countryCode": "US",
    "latitude": 28.02,
    "longitude": -82.11
  }
}
```

- `name` is a cleaned, human-friendly name; `organization` is the raw dataset description.
- `countryCode` is where the network is **registered**, not the visitor's location.
- `isHosting` is true for known cloud / datacenter / VPN-provider networks (`Data/Networks/HostingNetworks.cs`).
  It is conservative: `false` does not prove an address is residential.
- `network` is `null` for private addresses (`isPublic: false`) and for public addresses not in the dataset.
- `location` is roughly where the address is, from the DB-IP file: the country is usually right, the city is
  often the provider's nearest hub, and the coordinates are the city's centre, rounded to 2 decimals (about
  1 km). It is never a street address. Any part the file doesn't know is `null`, and `location` itself is `null`
  for private addresses, addresses not in the file, or when the file isn't loaded or location is turned off.
  `dataUpdatedAt` refers to the network dataset only.
- Invalid input returns `400`, a missing network dataset returns `503`. A missing location file doesn't fail
  lookups; they just have no `location`.
- **If you show `location` on a web page, you must link to DB-IP** (the file's licence, CC BY 4.0), e.g.
  `<a href="https://db-ip.com">IP Geolocation by DB-IP</a>`.

## Configuration

All settings live under `Tools:IspChecker` and can be set with environment variables
(`Tools__IspChecker__<Name>`). List settings take one comma-separated value
(`Tools__IspChecker__TrustedProxies=172.18.0.0/16, 10.0.0.5`) or indexed entries (`__0`, `__1`, …).
Invalid values stop the app at startup with a message naming the bad entry.

| Setting | Default | Description |
|---|---|---|
| `DataFilePath` | `data/ip2asn-combined.tsv.gz` | Dataset path; relative paths resolve against the content root. Use an absolute path in production. |
| `LocationDataFilePath` | `data/dbip-city-lite.mmdb` | DB-IP "IP to City Lite" file (MMDB format) for `location`. Empty turns location off. A missing file only leaves `location` out. MaxMind's GeoLite2 City file uses the same layout and works too. |
| `TrustedProxies` | *(empty: loopback only)* | Reverse proxies allowed to set `X-Forwarded-For`, as IPs or CIDRs, e.g. `172.18.0.0/16`. Replaces the loopback default when set. |
| `AllowedOrigins` | *(empty: CORS off)* | Sites whose browser JavaScript may call the API, e.g. `https://comparevpn.co`. Supports `https://*.example.com` and `*`. |
| `ReloadCheckInterval` | `00:01:00` | How often to check the dataset files for changes. `00:00:00` disables reloading. |
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

### Docker

```bash
docker build -t comparevpn-tools .
docker run -p 8080:8080 -e Tools__IspChecker__AllowedOrigins=https://example.com comparevpn-tools
```

The image is ready to run on its own:

- It contains a copy of the datasets (network list and location file) from when it was built, so lookups work
  as soon as it starts. If DB-IP can't be reached during the build, the image is still built and lookups have
  no `location` until the first refresh succeeds.
- A small loop in the container downloads a fresh network list right after start and then every
  `DATASET_REFRESH_INTERVAL` (default `24h`; a number with `s`, `m`, `h` or `d`). The location file is only
  downloaded again when a new month's is published. Set the interval to `0` if you mount the datasets and
  refresh them yourself. A failed download is logged and retried next time; the app keeps serving the data it
  has.
- `-e Tools__IspChecker__LocationDataFilePath=` (empty) turns location off, and the loop then skips that
  download too.
- It listens on port `8080` as the unprivileged `app` user, and its Docker `HEALTHCHECK` asks `/healthz`.
- `GET /version.txt` shows the version passed at build time (`--build-arg APP_VERSION=v1.2.3`), or `dev`.
  Deploy scripts can wait for it to know the new version is the one answering.

All settings below work as environment variables (`-e Tools__IspChecker__...`).

### Dataset

Without Docker, the datasets are not part of the publish output. Keep them outside the app directory and
point `DataFilePath` and `LocationDataFilePath` at them:

```bash
# Once, and then daily (e.g. cron). Files are replaced atomically; the location file (about 60 MB to
# download) is only fetched again when a new month's is out. SKIP_LOCATION_DATA=1 downloads only the
# network list.
scripts/download-data.sh /var/lib/comparevpn-tools
Tools__IspChecker__DataFilePath=/var/lib/comparevpn-tools/ip2asn-combined.tsv.gz
Tools__IspChecker__LocationDataFilePath=/var/lib/comparevpn-tools/dbip-city-lite.mmdb
```

The script stops with an error if the network list can't be downloaded, but a failed location download is
only a warning: the current location file (if any) stays in use. It writes `dbip-city-lite.month` next to the
location file to remember which month it has.

**No restart is needed after a refresh.** The app checks the file's modification time every
`ReloadCheckInterval`, loads a changed file in the background and swaps it in once it has fully
loaded. Lookups are never interrupted, and a missing, empty or corrupt replacement is logged and
ignored, so the previous data keeps serving. The check polls rather than relying on file-system
events, so it also works on Docker Desktop bind mounts, NFS and Kubernetes volumes.

If the file is missing at startup the app still starts, logs a warning with the path it tried,
returns `503` from the API and picks the file up as soon as it appears.

The location file (`LocationDataFilePath`) is checked and swapped in the same way. Without it the API
answers normally with `"location": null`, and logs one warning with the path it tried; set
`LocationDataFilePath` to an empty value to turn location off and silence that warning.

Memory: about 200 MB with the network dataset loaded, peaking around 300 MB while a reload holds both
copies. The location file (about 130 MB) is opened as it is rather than loaded into lists, so its pages are
shared with the operating system's file cache and only the parts used are read in. A container limit of
384 MB leaves enough headroom.

### Health check

`GET /healthz` returns:

| Status | HTTP | Meaning |
|---|---|---|
| `Healthy` | 200 | Dataset loaded and fresh (the optional location file doesn't affect health) |
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

### Finding the right trusted proxies

On a hosting service you may not know the proxy's address in advance. The app tells you: with
`TrustedProxies` unset, call `GET /api/tools/isp` from your own computer. If `ipAddress` is a private
address (`isPublic: false`), that is the proxy in front of the app. Trust its network, e.g.
`Tools__IspChecker__TrustedProxies=169.254.0.0/16`, and check again: it should now show your own
address and provider.

Also turn off the hosting service's request logs for this app (or keep them free of addresses):
the app itself never logs visitors' IPs, and its host shouldn't either.

### Deploying to Azure App Service

`.github/workflows/deploy.yml` is how the CompareVPN instance is deployed; it is optional for
self-hosters. Pressing **Run workflow** on `master` runs the tests, has Azure Container Registry build
the image as the next `v1.0.N`, switches an App Service web app to it, waits for `/version.txt` to show
the new version, checks `/healthz` and a real lookup, and switches back to the previous image if
those checks fail. It reads every name and address from repository settings (listed at the top of the
workflow), so the repository holds none. The Azure names and IDs are repository secrets rather than
variables, so they stay hidden in the run logs, which are public on a public repository.

The web app must run a single container (not Azure's sidecar mode), pull from the registry with its
managed identity, and have `WEBSITES_PORT=8080`, the health check path `/healthz`, and the settings above
(`Tools__IspChecker__AllowedOrigins`, `Tools__IspChecker__TrustedProxies`).

## Browser code (`web/`)

Some tools run entirely in the visitor's browser, with no API endpoint. Their code lives in `web/` as plain
TypeScript with no framework and no page markup, so any website can use it and anyone can check what it does.
It is tested here; a website copies the files into its own build.

### WebRTC leak test (`web/webrtc-leak-test/`)

WebRTC is the browser feature behind video calls. To set up a call, the browser lists every address it can be
reached on, and any website can ask for that list without a permission prompt. If a VPN doesn't cover WebRTC,
the list can include the visitor's real address: a WebRTC leak.

| File | What it does |
|---|---|
| `WebRtcProbe.ts` | Prepares a WebRTC connection that never connects (no camera or microphone) and collects the addresses the browser lists. Answers "unavailable" when WebRTC is turned off or blocked. |
| `IceCandidate.ts` | Reads one address line from the browser and says whether the address is public, local, or hidden behind the browser's random `….local` name. |
| `LeakVerdict.ts` | Compares the public addresses WebRTC revealed with the address websites see. A different address counts as a leak unless both belong to the same network (e.g. one provider's IPv4 and IPv6 addresses). |

A page using it:

1. Looks up the visitor's own address (e.g. with `GET /api/tools/isp`), which is what websites see.
2. Runs `new WebRtcProbe().run()`.
3. Looks up each address from `LeakVerdict.addressesToLookUp(seenAddress, probe)` (e.g. `GET /api/tools/isp/{ip}`).
4. Calls `LeakVerdict.decide(seen, probe, lookups)`: `leak`, `no-leak` or `webrtc-blocked`, with the leaked
   addresses, local addresses the browser shows openly, and whether the visitor's address looks like a VPN.

**Privacy:** to learn its public address, the browser asks an address server (a "STUN server"), which sees the
visitor's address, as with every WebRTC leak test. The probe uses Cloudflare's free public server
(`stun:stun.cloudflare.com:3478`) unless you pass your own, e.g. `new WebRtcProbe(['stun:stun.example.org:3478'])`.
Nothing else is sent anywhere by this code.

```bash
cd web
npm ci
npm run typecheck
npm test
```

## Repository layout

```
CompareVPN.ISPLookup/               Web application (the only project in the solution)
  Addresses/                        Reading and classifying IP addresses
  Data/Networks/                    Network list (iptoasn.com), hosting list, provider-name tidying
  Data/Location/                    Approximate location (DB-IP City Lite)
  Web/                              Allowed websites (CORS) and trusted proxies settings
  Tools/IspChecker/                 The ISP checker: rules, answer types, endpoints and set-up
  wwwroot/index.html                ISP lookup page
CompareVPN.Tools.IspChecker.Tests/  Standalone test project
web/                               Browser code for tools that run in the visitor's browser, with its tests
  webrtc-leak-test/                The WebRTC leak test
.github/workflows/                 CI, release artifact, and the optional Azure deploy
scripts/                           Dataset download helper and the container's start script
Dockerfile                         Container image (app + datasets + daily refresh)
data/                              Local datasets (network list and location file; never committed)
```

Keep ISP lookup features in this application. Future tools can be added to the repository
when needed; separate libraries are unnecessary unless code needs to be distributed independently.

## Development

```bash
dotnet build
dotnet test CompareVPN.Tools.IspChecker.Tests/CompareVPN.Tools.IspChecker.Tests.csproj
```

The tests are kept outside the application solution so it contains only the runnable project.
CI runs the test project explicitly, and the browser code's checks in `web/` (`npm run typecheck`,
`npm test`). Release tags such as `v0.1.0` build an application publish
artifact instead of NuGet packages.

## License

[MIT](LICENSE), including the browser code in `web/`. The iptoasn.com dataset is released under the PDDL (public domain).

The location data is the "IP to City Lite" file by [DB-IP](https://db-ip.com), licensed under
[Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/). It may be used in any
application, including commercially, as long as DB-IP is credited; web pages that show or use its results
must link to https://db-ip.com. The file is downloaded, never committed. It is read with the
[MaxMind.Db](https://github.com/maxmind/MaxMind-DB-Reader-dotnet) package (Apache 2.0).
