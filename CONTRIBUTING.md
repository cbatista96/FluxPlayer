# Contributing

Use English for source comments and engineering documentation. Keep public product documentation, the changelog and release notes in English and Spanish, with English first on the GitHub release page. Describe the user-visible problem and expected behavior before proposing a change. For a playback issue, include the Windows version, source container/codec, relevant settings and reproduction steps. Share media only if you have permission to distribute it.

Build the solution and run the relevant checks in [Testing](docs/TESTING.md). UI changes also need a manual check of the affected flow. Keep optional tools lazy, avoid unbounded background work and preserve native callback lifetime rules described in [Architecture](docs/ARCHITECTURE.md).

Update `FEATURES.md` when capability or status changes. A new checkbox, flag or settings panel does not complete a feature without working behavior. Explain any remaining limitation and the validation actually performed in the pull request.

Keep application binaries, installers, private media, local settings, logs, credentials and generated test reports out of Git. Use GitHub Releases for public binary downloads.

By contributing, you agree that your contribution is distributed under the project's GPL v3.0 license. Third-party material must retain its attribution and compatible license information.
