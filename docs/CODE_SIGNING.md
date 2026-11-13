# Windows code signing

GitHub's SHA-256 checksums detect changes to downloaded bytes. They are not an Authenticode signature or a verified publisher identity. The first GitHub release is unsigned. Windows can warn about the executable; the project does not ask you to disable SmartScreen.

For signed desktop builds, configure a trusted Windows code signing identity on the release host. The release script can use a Windows certificate store thumbprint to timestamp and verify FluxPlayer-owned binaries, the installer and its uninstaller. For service-based signing, use the provider's protected workflow and verify its output before publishing. Keep private keys out of Git and out of release assets. Preserve existing upstream signatures and generate hashes only after signing.

Only reviewed source should be released with the publisher's identity. Review the release artifacts and signing logs before publication. A self-signed certificate is suitable for isolated testing and does not establish public trust.

The Microsoft Store MSIX follows a separate path. Microsoft signs the Store copy after certification; its signature does not apply to the GitHub EXE or ZIP. See [Microsoft's signing options](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options).
