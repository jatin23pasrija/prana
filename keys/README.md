# Keys

`catalogue-signing.pub` is the public half of the catalogue signing keypair. It is committed on
purpose: the app compiles it in and refuses any catalogue package that does not verify against it.

The private half is never here. It exists only as the `CATALOGUE_SIGNING_KEY` repository secret,
and a copy in the maintainer's offline backup. See "Catalogue signing" in
[SECURITY.md](../SECURITY.md) for how it is generated, used and rotated.

Verifying a release needs nothing from this project:

```bash
openssl dgst -sha256 -verify keys/catalogue-signing.pub \
  -signature catalogue.db.br.sig catalogue.db.br
```
