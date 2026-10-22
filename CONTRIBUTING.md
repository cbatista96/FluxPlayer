# Contributing

Use English for application text, comments, documentation, issues and pull requests. Describe the user-visible problem and expected behavior before proposing a change. For a playback issue, include the Windows version, source container/codec, relevant settings and reproduction steps. Share media only if you have permission to distribute it.

Build the solution and run the relevant checks in [Testing](docs/TESTING.md). UI changes also need a manual check of the affected flow. Keep optional tools lazy, avoid unbounded background work and preserve native callback lifetime rules described in [Architecture](docs/ARCHITECTURE.md).

Update `FEATURES.md` when capability or status changes. A new checkbox, flag or settings panel does not complete a feature without working behavior. Explain any remaining limitation and the validation actually performed in the pull request.

Do not commit binaries, build output, installers, private media, local settings, logs, credentials or generated test reports. This project currently publishes source only; releases and version tags require an explicit maintainer decision.

By contributing, you agree that your contribution is distributed under the project's GPL v3.0 license. Third-party material must retain its attribution and compatible license information.
