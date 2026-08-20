# Contributing

Instructions for people **maintaining** this SDK. If you only want to *use* it,
read the [User Guide](docs/user-guide.md) instead.

## Development setup

From the repository root:

```bash
dotnet restore Pagr.Sdk.slnx
dotnet build Pagr.Sdk.slnx -c Release
```

Requires the **.NET 8 SDK or later**. Note that `Pagr.Sdk.slnx` uses the XML
solution format, which needs SDK **9.0.200 or newer** to parse — if `dotnet
build` cannot read the solution, that is why. The library itself targets
`net8.0` and has no NuGet dependencies; `Microsoft.SourceLink.GitHub` is a
build-time-only reference (`PrivateAssets="All"`) and never reaches consumers.

The test, example and smoke-test projects set `<RollForward>Major</RollForward>`
so they still run on a machine that has no .NET 8 runtime installed.

### Running tests

```bash
dotnet test Pagr.Sdk.slnx -c Release
```

No live API is needed — the suite is fully self-contained.

## Testing conventions

- **Mock at the `HttpMessageHandler` layer** via the helpers in
  `Pagr.Sdk.Tests/TestFixtures.cs`, never by monkeypatching or subclassing the
  client. This exercises path building, query-parameter cleaning, and error
  mapping for real. `Pagr.Sdk.Tests` is a friend assembly
  (`InternalsVisibleTo`), so internals are reachable without widening the public
  surface.
- **Cover both paths:** the happy JSON response *and* at least one error status
  mapping to its typed exception. Every `PagrApiException` subclass should have
  a test that provokes it.
- **Assert the URL**, not just the parsed result — a route regression is easy to
  miss otherwise.
- **Binary/PDF branches get their own test.** Streaming bytes, header metadata,
  and `SaveAsync` path handling do not share a code path with the JSON branch.
- **Business outcomes are not exceptions.** A failed validation or insufficient
  credit comes back as data on the result object; assert that it does *not*
  throw.
- Model tests should assert camelCase→PascalCase mapping, enum fail-open
  behaviour, and default handling.

`Pagr.Sdk.SmokeTest` and `Pagr.Sdk.Examples` hit a **live** API and need a real
API key. They are run manually only and are never part of an automated test run
— CI builds them but does not execute them.

## Build & release

```bash
dotnet build Pagr.Sdk.slnx -c Release -warnaserror
dotnet test Pagr.Sdk.slnx -c Release --no-build
dotnet pack Pagr.Sdk -c Release -o ./artifacts
```

`dotnet pack` produces both `Pagr.Sdk.<version>.nupkg` and a matching `.snupkg`
symbol package. The version is set by `<Version>` in `Pagr.Sdk/Pagr.Sdk.csproj`
— a single source of truth. The packed `README.md` is the repository-root one,
referenced as `..\README.md`; keep its links absolute, because relative links
resolve on GitHub but are dead on a package page.

Release checklist:

1. Bump `<Version>` in `Pagr.Sdk/Pagr.Sdk.csproj` (SemVer).
2. Update `README.md` and the [User Guide](docs/user-guide.md) if the surface
   changed.
3. Add a `CHANGELOG.md` entry.
4. `dotnet build -warnaserror` and `dotnet test` green on both Linux and Windows
   (CI runs an OS matrix — the SDK does real filesystem path work).
5. `dotnet pack`, then inspect the `.nupkg`: it must contain
   `lib/net8.0/Pagr.Sdk.dll`, `lib/net8.0/Pagr.Sdk.xml`, and `README.md`, and its
   nuspec must list **no dependencies**.
6. Tag the release.

**Versioning policy.** The SDK follows SemVer: a breaking change to the public
surface (a removed/renamed export, a changed method signature or return type, or
a behavioural change consumers rely on — e.g. a field that used to be a `string`
becoming an enum) bumps the **major** version; additive, backward-compatible
changes bump **minor**; fixes bump **patch**. Record consumer-visible changes in
`CHANGELOG.md` per release so integrators can see what moved.

One .NET-specific trap worth naming: `PagrApiClient.DefaultBaseUrl` is a
`public const`, so the C# compiler **inlines its value into every consumer
assembly at build time**. Changing it does not reach a consumer who has not
recompiled. Treat it as a breaking change — major version — not a
configuration tweak.
