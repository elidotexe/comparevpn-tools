# CompareVPN Tools

Open-source backend and API endpoints for the free tools on the CompareVPN website, starting with the ISP
checker ("What is my ISP?"). The CompareVPN site builds the visible tool pages and calls these endpoints from the
visitor's browser. Anyone can read, run or self-host this code, so it is written to be understood by people who
have never seen the CompareVPN site.

- `CompareVPN.ISPLookup/`: the app (ASP.NET Core minimal API, .NET 10, no database). Each tool lives in its own
  folder under `Tools/` (now only `Tools/IspChecker/`); what tools share sits beside it in `Data/`, `Addresses/`
  and `Web/` (see "How the code is organised"). `wwwroot/index.html` is a small demo page, not the production
  page.
- `CompareVPN.Tools.IspChecker.Tests/`: NUnit + Moq tests. They are kept out of `CompareVPN.Tools.slnx` on
  purpose, so run them by path.
- Build: `dotnet build` (from repo root). `TreatWarningsAsErrors` is on, so a warning fails the build.
- Run tests: `dotnet test CompareVPN.Tools.IspChecker.Tests/CompareVPN.Tools.IspChecker.Tests.csproj`
- Run locally: `./scripts/download-data.sh`, then `dotnet run --project CompareVPN.ISPLookup` (port 5180).
- Package versions live in `Directory.Packages.props`; shared build settings in `Directory.Build.props`.
- Formatting: follow `.editorconfig` (4-space indent in C#).

## Open source: what that means for every change

- **Visitor privacy comes first.** IP addresses are never logged, stored or sent to another service. Lookups
  run against local data only. API responses use `Cache-Control: no-store`. Rate limiting may hold an address in
  memory as a key for the current window, nothing more. A change that breaks any of this is a bug, even if it
  makes something easier.
- **Nothing private goes into the repository**: no secrets, keys, connection strings, server names, internal
  business plans or traffic and affiliate figures. Settings that differ per server come from configuration
  (`Tools__IspChecker__<Name>` environment variables), with safe defaults in `appsettings.json`.
- **`docs/` is private and git-ignored**: it holds the owner's tool plans and keyword research. Read it for
  context, but don't quote its figures or plans in code, comments, the README, commits or pull requests, and
  don't move anything from it into a tracked folder.
- **Self-hosting must keep working**: the app starts with only the .NET SDK and the downloaded dataset, without
  anything from the CompareVPN site. Don't add a dependency on CompareVPN's servers, accounts or paid services.
- **The API is a public contract.** Other people's code (and the CompareVPN site) reads the response fields,
  status codes and settings names. Don't rename or remove them; add new optional fields instead. If a breaking
  change is really needed, it gets a new version of the address (e.g. `/api/v2/...`) and a note in the release.
- **Keep the README in step with the code** in the same commit: the API table and example response, the
  settings table, deployment notes and the repository layout.
- **Downloaded datasets are never committed** (`data/*` is git-ignored). Check that a dataset's licence allows
  redistribution and commercial use before adding a new data source, and credit it in the README.
- Comments, commit messages and pull requests are in English and written for strangers: don't refer to private
  code, internal tickets or the CompareVPN site's internals.

## How we work

This applies to every session: local, Remote Control from the phone, and cloud sessions (claude.ai/code).

- **Branches and pushing:** the main branch is `master`. Cloud sessions work on a branch and open a pull request
  into `master`; never push to `master`. Local sessions commit but never push unless asked: the owner pushes. Never
  create or push release tags.
- **Tests:** `.github/workflows/ci.yml` builds the solution and runs the test project on every push to `master` and
  every pull request. Run the same commands (`dotnet build -c Release` and the `dotnet test` command above)
  before opening a pull request, and only open it when they pass.
- **Commits:** one feature per commit, and bug fixes in their own commit, separate from refactors. Commit with an
  explicit list of the files you changed (`git commit -m "..." -- path1 path2`), so nothing else slips in.
- **Order of work:** pin down current behaviour with tests, then change the code. For a bug, write the failing
  test first, then fix it.
- **Questions:** only ask about real product decisions (wording, which behaviour to keep, what the public API
  returns). Otherwise pick the sensible default and say which one you picked.
- **Reports:** short and plain: the commits and what each changed, any bugs found, and what to try before
  releasing.

### Releasing

- Only the owner releases, by pushing a `vX.Y.Z` tag. `.github/workflows/release.yml` runs the tests and
  publishes the app as a build artifact.
- Only the owner deploys, by pressing **Actions → Deploy → Run workflow** (`.github/workflows/deploy.yml`). It
  builds the `Dockerfile` in Azure as the next image `v1.0.N` (registry tags, not git tags), switches the web app,
  waits for `/version.txt`, smoke-tests `/healthz` and a lookup, and switches back if that fails. Never run `az`
  commands that change production. All Azure names and IDs come from repository secrets (not variables, which
  show in the public run logs): never write them into the workflow, any tracked file or a log line.
- The dataset is not part of the build output. The container image downloads a copy while it is built and
  refreshes it every `DATASET_REFRESH_INTERVAL` (`scripts/docker-entrypoint.sh`); servers without Docker
  refresh it with `scripts/download-data.sh` (e.g. a daily job). The app picks up the new file without a restart.
- After changing the `Dockerfile` or the start script, build the image and run it (`docker build -t
  comparevpn-tools . && docker run -p 8080:8080 comparevpn-tools`), then check `/healthz` and a lookup.

### Lessons learned

- **Test on a bare machine too:** GitHub's runner and a fresh server have none of this computer's tools or
  files, including the downloaded dataset in `data/`. Tests create their own small data files in a temporary
  folder; never make a test depend on the real dataset. If a test might depend on the machine, run it in
  `mcr.microsoft.com/dotnet/sdk:10.0`.
- **Memory:** the loaded dataset takes about 200 MB, and about 300 MB while a reload holds both copies. Keep an
  eye on this when changing how data is loaded or adding a second dataset.

## How the code is organised

The app has one folder per tool, and folders for what tools share. Each folder is its own namespace
(`CompareVPN.<Folder>`, e.g. `CompareVPN.Data.Networks`):

```
CompareVPN.ISPLookup/
  Addresses/        Reading and classifying IP addresses (private or public, typed input)
  Data/             Downloaded data files that reload while the app runs
    Networks/       Which network owns an address (iptoasn.com), the hosting list, tidy provider names
    Location/       Roughly where an address is (DB-IP City Lite)
  Web/              Reading web settings: allowed websites (CORS) and trusted proxies
  Tools/
    IspChecker/     The ISP checker: its rules, answer types, and AspNetCore/ (endpoints and set-up)
```

Put code in `Data/`, `Addresses/` or `Web/` when it is about addresses, datasets or web settings in general,
even if only one tool uses it today; keep in `Tools/<Tool>/` what only makes sense for that tool (its answer,
its rules, its endpoints and settings). Tools may use the shared folders, but a shared folder never uses a tool.

Each tool follows the same split, which matches controller → service → repository on the main site:

- **Endpoints** (`Tools/<Tool>/AspNetCore/...EndpointRouteBuilderExtensions.cs`): like a controller, they only
  handle the web request: reading the address or input, choosing the status code, response headers
  (`no-store`) and problem responses. No lookup rules here.
- **The tool itself** (e.g. `IspChecker`): like a service, it holds the tool's rules and puts the answer
  together from the shared pieces (address checks, data sources, the hosting list, name tidying). It knows
  nothing about web requests, so it can be tested directly.
- **Data sources** (in `Data/`, e.g. `IAsnDataSource`, `IpToAsnFileDataSource`, `DbIpCityFileDataSource`): like
  a repository, they find records in a dataset and nothing else.
- **Set-up** (`...ServiceCollectionExtensions.cs`, `...Options.cs`, `...OptionsValidator.cs`): registers the
  tool, reads and checks its settings, and adds what it needs (rate limiting, CORS, trusted proxies, health
  check, background reloading). `Program.cs` stays a short list of these calls.
- **A new tool** gets its own folder `Tools/<Tool>/` with the same split, its own settings section
  `Tools:<Tool>`, endpoints under `/api/tools/<tool>`, its own test project
  `CompareVPN.Tools.<Tool>.Tests` (added to both workflows), and its own README section. It uses `Data/`,
  `Addresses/` and `Web/` rather than copying from another tool. Set-up code (rate limiting, CORS, reloading
  in `Tools/IspChecker/AspNetCore/`) moves to a shared place only once a second tool actually needs it. Don't
  create separate libraries unless code has to be distributed on its own.
- **The demo page** (`wwwroot/index.html`) stays a single file with no build step and no outside scripts or
  fonts. It shows that the API works; the real page with the guide and design lives on the CompareVPN site.

## Code style: "read like a book"

Any file should read top to bottom, by a non-technical person, like a short chapter. Apply this style to every
file you create or substantially change. The existing code predates this style and is being brought in line
with it.

### Refactoring approach

- Work **feature by feature**, top to bottom: endpoint → tool → data source for one feature, then the next.
  Don't refactor a whole layer at once.
- Start each feature from its endpoint: the endpoints are the feature's table of contents and show which code is
  actually used.
- **Endpoints only handle the web request** (see above). Rules and data access live in the tool and its data
  source.
- The tool reports outcomes the endpoint needs to react to with small result enums or records (e.g. "dataset not
  loaded yet", "not a public address") rather than knowing about status codes. When a method needs many
  inputs, group them in a record. One type per file. In the endpoint, `switch` on every value by name and make
  `default` throw, so a newly added outcome can't silently return the wrong answer.
- Moving rules out of the endpoints makes them testable without a web request: add tests for the rules you move.
- **Error handling**: log each error once, centrally; only catch errors you can actually handle.
  - Unexpected errors during a request are left to ASP.NET Core's own error handling, which logs them. If a
    custom error response or extra log details are needed, add them in one central place in `Program.cs`
    (`UseExceptionHandler`, `AddProblemDetails`), never as `try/catch` in each endpoint. Error logs must not
    contain the visitor's IP address.
  - No blanket `try/catch` and **no log-and-rethrow** (`catch (Exception e) { _logger.LogError(e, ...); throw; }`):
    it logs the same error twice and adds boilerplate without handling anything. Never catch `Exception` and
    return `null`/`false`/empty; that makes a broken dataset look like "not found".
  - Only catch specific, expected failures you can respond to (turn into a normal outcome, fall back, clean up),
    with a `when` filter where it narrows the case, and explain in a comment why that case is safe to handle.
  - Exception: work that runs outside a web request (the dataset reload in `IspCheckerDatasetService`, timers)
    has no central handler, so it catches and logs its own errors, once per distinct problem, and keeps the app
    running on the data it already has.
  - Expected outcomes like "not found" or "not a public address" use `null` or result values, not exceptions.
  - Bad settings stop the app at startup with a message naming the bad entry (`...OptionsValidator`), rather
    than failing later on a visitor's request.
- **Code without tests**: before refactoring it, write tests that pin down how it behaves now, run them against
  the old code, then refactor and run them again. Fix bugs found this way in a separate commit from the refactor.
- **Refactoring code that produces output** (API responses, cleaned ISP names): record the output for varied
  input before and after the change and compare it exactly, using a temporary test that isn't committed.
- Delete dead code you find along the way (check callers with grep and `git log -S` first) instead of
  documenting it.
- Behaviour must not change during a style refactor. Keep the tests passing; one feature per commit.

### Patterns we've settled on

- **Interfaces**: give a class an interface only when a test swaps it for a pretend one (`Mock<IXxx>`) or when
  self-hosters are meant to plug in their own version (e.g. `IAsnDataSource` for another dataset). Otherwise
  register and inject the class itself.
- **Constructors**: a class that only stores what it's given uses a primary constructor
  (`public sealed class IspChecker(IAsnDataSource dataSource)`); values worked out from those go in field
  initializers. Keep an ordinary constructor only for real set-up work. No `ArgumentNullException` guards on
  injected services: the container never passes null. Nullable reference types are on, so let the compiler
  catch nulls inside the app; keep `ThrowIfNull` only on public methods that outside code may call.
- **Time**: read the clock through the injected `TimeProvider`, never `DateTime.Now`, so tests can set the time.
- **Settings**: every setting has a default in `appsettings.json`, a check in the options validator, a row in
  the README's settings table, and can be set from one environment variable (comma-separated lists are
  accepted).
- **Data that changes while running** (the dataset): load the new copy fully in the background, then swap it in
  at once. A missing or broken new file never replaces a good one.
- **`sealed` and `internal`**: classes are `sealed` unless built to be extended, and `internal` unless outside
  code is meant to use them.

### Class summary (every class)

A `/// <summary>` above the class, in plain, non-technical language:

1. One or two sentences on what the class is for, using an everyday comparison where it helps
   (e.g. "a phone book that turns an address into the company that owns it").
2. "In plain terms, here is what happens:" followed by numbered steps (`1.`, `2.`, …) describing the main flow.
3. Mention what happens when things go wrong if that's part of the behaviour (e.g. "keeps answering from the
   old data so the website keeps working").

Avoid jargon (HTTP, JSON, DI, CIDR, ASN, etc.) in the class summary; say "the web address", "the list of
networks", "the network that owns the address". When a technical term is unavoidable, explain it once in
everyday words.

### Method summaries (every public method, and non-obvious private ones)

A short `/// <summary>` saying what the method does and what it returns, in plain language. Explain each
parameter in everyday words when its name isn't self-explanatory. Precise terms (`null`, `IPAddress`, status
codes) are fine here, since developers read them.

### Small functions with step names

- Split long methods into small private methods whose names read like steps:
  `ReadVisitorAddress()`, `FindNetwork()`, `CleanUpName()`. The calling method should read like a numbered
  list.
- Aim for methods short enough to read at a glance (roughly 20–30 lines max). One method, one job.
- Order members in reading order: public entry points first, then the private helpers they call, in the order
  they are called.

### Step comments

- Mark the main steps inside a method with `// Step N: ...` comments that match the numbers in the class summary.
- In classes with several entry points (e.g. the endpoints), the class summary numbers the entry points
  (1, 2, 3…), each one's method summary starts with "Step N:", and the steps inside it are sub-steps:
  `// Step 2a:`, `// Step 2b:`, … Every entry point gets sub-step comments, not just the long ones.
- Group private helpers under section dividers that say which steps they serve, e.g.
  `// ───── Helpers for reading the dataset file (steps 2–3) ─────`.
- Briefly explain lines whose purpose isn't obvious (e.g. a binary search, a bit shift, an address range check).
- Do **not** comment every line or restate what a well-named method call already says. Comments that just repeat
  the code go out of date; prefer a better name over a comment.
- When changing code, update its comments in the same edit so they never describe old behaviour.

### Example

```csharp
/// <summary>
/// This class answers "which internet provider is behind this address?", like looking a street address up in
/// a directory to find who owns the building.
///
/// In plain terms, here is what happens:
///   1. It tidies the address so old and new address formats are treated the same.
///   2. It checks whether the address is private (a home network or this computer), which has no provider.
///   3. It looks the address up in the downloaded list of networks.
///   4. It turns the network's registered name into the name people know, and notes whether it is a hosting
///      company rather than a home internet provider.
///   If the address isn't in the list, it still answers, just without a provider.
/// </summary>
public sealed class IspChecker(IAsnDataSource dataSource)
{
    /// <summary>
    /// Finds the provider behind <paramref name="address"/>. Returns the result with <c>Network</c> set to
    /// <c>null</c> for private addresses and addresses the dataset doesn't know.
    /// </summary>
    public async ValueTask<IspCheckResult> CheckAsync(IPAddress address, CancellationToken cancellationToken)
    {
        // Step 1: Treat "::ffff:8.8.8.8" the same as "8.8.8.8".
        var tidied = Tidy(address);

        // Step 2: Private addresses have no provider to look up.
        if (!IpAddressClassifier.IsPublic(tidied)) return PrivateAddressResult(tidied);

        // Step 3 and 4: Look it up and describe what we found.
        var match = await dataSource.FindAsync(tidied, cancellationToken);
        return DescribeNetwork(tidied, match);
    }
}
```

Reference files already in this style, under `CompareVPN.ISPLookup/`:
`Tools/IspChecker/AspNetCore/IspCheckerEndpointRouteBuilderExtensions.cs` (endpoints), `Tools/IspChecker/IspChecker.cs`
(the tool, the full version of the example above), `Data/Networks/IpToAsnFileDataSource.cs` (a data source) and
`Tools/IspChecker/AspNetCore/IspCheckerDatasetService.cs` (background work).
Outcomes are reported as in `AsnSearchResult` / `IspCheckAnswer`: an enum plus a record with small factory methods.
