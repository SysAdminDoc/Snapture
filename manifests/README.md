# Snapture winget manifests

Three-file manifest set per release that targets the [winget multi-file YAML schema 1.7.0](https://learn.microsoft.com/windows/package-manager/package/manifest).

To submit a new version to `microsoft/winget-pkgs`:

1. Cut the GitHub release for the new version (the existing `release.yml` workflow does this).
2. Compute the SHA-256 of the `Snapture-vX.Y.Z-win-x64.zip` release asset:
   ```pwsh
   (Get-FileHash .\Snapture-vX.Y.Z-win-x64.zip -Algorithm SHA256).Hash
   ```
3. Copy `manifests/SysAdminDoc/Snapture/<previous-version>/` to a new folder for the new version.
4. Update `PackageVersion` in all three YAML files; update `InstallerUrl` and `InstallerSha256` in the `.installer.yaml`; update `ReleaseDate` and `ReleaseNotesUrl`.
5. Validate locally:
   ```pwsh
   winget validate --manifest manifests/SysAdminDoc/Snapture/X.Y.Z/
   ```
6. Fork `microsoft/winget-pkgs`, copy the new version folder under `manifests/s/SysAdminDoc/Snapture/X.Y.Z/`, open a PR.

Why portable+zip and not MSIX:

- The MSIX route requires `runFullTrust` for the `RegisterHotKey` call. MSIX-with-runFullTrust signing chain wants either a CWS-style identity or an EV cert.
- Portable ZIP via the existing GitHub release artifact is verifiable end-to-end against the SHA-256 in the manifest and works with `winget upgrade --all` for users who installed via this channel.
- MSIX manifest will land alongside this one in v0.7 once the SignPath OSS submission completes.
