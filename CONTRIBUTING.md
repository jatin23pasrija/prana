# Contributing to Prana

Thank you for helping. There are two very different kinds of contribution here, data and code,
and they have different rules. Read the part that applies to you.

[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies to everything.

---

## Contributing data

This is the most valuable thing you can do, and it needs no programming.

### From the app

Scan a product Prana does not know. The app searches approved open sources, shows you what it
found, and asks you to confirm it matches the packet in your hand. That confirmation becomes a
product request. Automation takes it from there.

You are not being asked to become a data administrator. You only confirm whether the product
looks right.

### From GitHub

Open an issue from the [issue chooser](https://github.com/jatin23pasrija/prana/issues/new/choose):

| Template | Use it when |
|---|---|
| Product request | A product is missing from the catalogue |
| Product correction | A value in the catalogue is wrong or out of date |
| Data source proposal | You know an open dataset we should be using |

### By pull request

If you are comfortable with Git, edit the record directly.

- One file per product, at `data/products/<first-3-digits-of-barcode>/<barcode>.json`.
- One product per pull request. It keeps review fast and honest.
- Run the validator before you push (see below). CI will run it anyway.

### The rules for data, without exception

1. **Never invent a number.** If the label does not state it, the value is `unknown`. An
   `unknown` is useful. A guess is damage.
2. **Never mix bases.** Per 100 g and per serving are different things. If you only have
   per-serving values and no serving mass, record it as per serving and say so.
3. **Keep the raw text.** The exact ingredient wording from the packet is evidence. The
   canonical form goes alongside it, never instead of it.
4. **Cite the source.** Every important field carries where it came from, when it was
   retrieved, and how confident we are.
5. **Photos of the packet are the best evidence.** A photo you took yourself is ideal. Do not
   upload photos you found on the internet.
6. **Do not copy from a source we have not cleared.** Publicly visible does not mean reusable.
   If the source is not listed as approved in [DATA_SOURCES.md](DATA_SOURCES.md), propose it
   first.

### Validating your data locally

```bash
# Check your records
dotnet run --project tools/Prana.Tools.Validator -- validate data

# Fix the formatting instead of arguing with CI about indentation
dotnet run --project tools/Prana.Tools.Validator -- format data
```

Every rule, every code and every tolerance is documented in
[docs/VALIDATION.md](docs/VALIDATION.md). CI runs the same tool and puts its findings on the
exact line of your pull request.

---

## Contributing code

### How this project is built

One feature at a time, in order, each on its own branch, each fully tested before the next one
starts. The full process is in [docs/planning/WORKFLOW.md](docs/planning/WORKFLOW.md) and the
feature list with every Definition of Done is in
[docs/planning/FEATURES.md](docs/planning/FEATURES.md).

Before touching code, read [docs/planning/DECISIONS.md](docs/planning/DECISIONS.md). It records
what has already been decided and why. If you want to change one of those decisions, that is a
pull request against that file with an argument, not a surprise in a feature branch.

### Setting up

You need the .NET 10 SDK and the MAUI workloads.

```bash
git clone https://github.com/jatin23pasrija/prana.git
cd prana
dotnet build
```

### Building the app

The app needs more than the tools do:

- The MAUI workloads: `dotnet workload install maui-android`
- The Android SDK
- **JDK 17 or newer.** Android manifest merging runs on the JVM, and an older JDK fails deep in
  the Android build with `UnsupportedClassVersionError` and a class file version number, which
  says nothing about the actual problem. JDK 11 is not enough.

  If `java -version` reports something older, point the build at a newer one rather than
  changing your system default. JetBrains IDEs keep downloaded JDKs under `~/.jdks`:

  ```bash
  export JAVA_HOME="$HOME/.jdks/corretto-18.0.2"     # or wherever yours lives
  export PATH="$JAVA_HOME/bin:$PATH"
  ```

  This matters more than it sounds. XamlC runs *after* manifest merging, so on a machine with an
  old JDK the Android build dies before XAML is ever compiled. XAML parsing is still checked, but
  type and converter errors are not, and they surface only in CI.
- A Mac, for iOS.

To produce an installable APK:

```bash
dotnet build app/Prana.Mobile/Prana.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```

It lands in `app/Prana.Mobile/bin/Release/net10.0-android/` at about 27 MB, signed with the
Android debug key. That is fine for testing and is not a release build; release signing is F17.

`dotnet build Prana.sln` builds everything including the app. CI builds `Prana.NoApp.slnf`
instead, which is the same solution without the app, so that a pull request touching only the
tools does not have to install a mobile toolchain. If you add a project outside `app/`, add it
to both or CI will silently never build it.

**Check the solution after running `dotnet sln add`.** It rewrites the whole file, and it has
twice silently dropped a project that was already there, including the MAUI app. The hygiene job
in CI catches it, but the cheaper place to notice is before pushing:

```bash
for p in $(find src tools tests app -name '*.csproj'); do
  grep -q "$(basename "$p")" Prana.sln || echo "MISSING: $p"
done
```

### The loop

```
1. Comment on the feature issue and agree the scope. Questions first, always.
2. Branch:  git checkout -b feat/fNN-slug
3. Small commits, Conventional Commits format.
4. Open a draft pull request early, so CI runs from the first push.
5. Run the feature test round for real. Device features need a real device.
6. Complete the Definition of Done checklist in the pull request.
7. Squash merge.
```

### Commit messages

```
feat(f03): add GTIN check-digit validation
fix(f09): handle camera permission denial on Android 14
data: correct sodium value for 8901234567890
docs: clarify unknown handling in the product schema
ci: run the validator only when data changes
```

Types: `feat`, `fix`, `data`, `docs`, `chore`, `ci`, `test`, `refactor`, `perf`.

### What a pull request must contain

- A link to the feature or issue it closes.
- What changed and why.
- The completed Definition of Done checklist.
- Real test evidence. For anything touching the phone, that means results from the phone, not
  a statement that it should work.
- Anything you deliberately left out, and why.

### Things that will get a pull request sent back

- A new dependency added without saying why in the description.
- Platform-specific code outside a platform abstraction.
- A health claim, a medical framing, or an absolute statement about a food in user-facing copy.
- Any credential, token or key, in any form, anywhere near the app.
- Data changes bundled into a code change.

---

## Reporting problems

- **Security vulnerability:** do not open a public issue. Follow [SECURITY.md](SECURITY.md).
- **Bug:** open a bug report with your device, Android version, app version and catalogue
  version.
- **Wrong data:** open a product correction. It is a data issue, not a bug.

## Licensing of your contribution

Code contributions are licensed under Apache-2.0. Data contributions are licensed under ODbL
1.0. By opening a pull request or submitting a product request you agree to that. There is no
separate contributor licence agreement to sign.
