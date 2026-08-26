# Security Policy

## Reporting a vulnerability

Do not open a public issue for a security problem.

Report it privately through
[GitHub Security Advisories](https://github.com/jatin23pasrija/prana/security/advisories/new),
or by email to **jeremie23scott@gmail.com**.

Please include what the problem is, how to reproduce it, and what an attacker could achieve.
You will get an acknowledgement within seven days. Fixes are published as a security advisory
once a release is available.

This project is maintained by volunteers. There is no bug bounty.

## What is in scope

- The mobile application, including anything that could leak user data off the device.
- The catalogue distribution chain: manifest, checksum, signature, download and installation.
- GitHub Actions workflows, especially anything that processes untrusted issue or web content.
- The contribution intake path.

## What is not a vulnerability

- A wrong nutrition value. That is a data correction, not a security issue.
- The absence of a feature.
- Anything requiring physical access to an unlocked device.

## Security principles this project holds to

These are commitments, not aspirations. A change that breaks one of them is a defect.

1. **The app never carries a GitHub write credential.** Not a token, not a key, not an
   obfuscated one. The app cannot write to this repository. Contribution goes through
   mechanisms that hold no repository authority in the client.
2. **Every catalogue is verified before it is used.** Size, SHA-256 and signature are all
   checked before the downloaded file is opened. An unverified package is deleted, never
   activated.
3. **A failed update never destroys a working catalogue.** Installation is atomic and the
   previous catalogue is retained until the new one is proven usable.
4. **External content is untrusted.** Web pages, API responses and issue text are all treated
   as hostile input. They are never interpolated into a shell command and never given
   privileged context in a workflow.
5. **Workflows run with least privilege.** The default token is read-only. Each job declares
   only the permissions it needs.
6. **User data stays on the device.** Grocery lists, history and preferences are local. Nothing
   is uploaded without an explicit action by the person using the app.

## Catalogue signing

Every catalogue release is signed. The private key exists only as a GitHub Actions secret and
is never present in the repository, in a build artefact, or on a developer machine. The public
key is compiled into the app.

The app refuses any package whose signature does not verify against the embedded public key,
regardless of where the package came from.

### Key rotation

Rotation is required if the key is suspected compromised, and is rehearsed once before the
first public release.

1. Generate a new keypair in a clean environment.
2. Publish an app version that trusts both the old and the new public key.
3. Wait for adoption of that version.
4. Start signing catalogue releases with the new key only.
5. Publish an app version that trusts only the new key.
6. Revoke and destroy the old private key, and record the rotation in a security advisory.

Because step 2 must reach devices before step 4, a compromised key is an app-release event, not
a catalogue-release event. This procedure is finalised in F06 before any release is published.

## App releases

The Android APK is signed with a release key held only in GitHub Actions secrets. Release
artefacts are published only from a tagged commit on `main`.
