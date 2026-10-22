# Contributing

Thank you for helping. A few rules keep Gannet what it is.

1. **Tests first.** Every change comes with a test that failed before it. `dotnet test` must be green and `dotnet format --verify-no-changes` clean.
2. **No result text on disk.** Search and reader results stay in memory for one run. Never write them to run records, checkpoints, logs or test fixtures. Fixtures are written by hand from each service's documented response shape.
3. **Citations come from source IDs.** The model cites `[S3]`; code checks and numbers citations. Do not let model output decide what a citation points at.
4. **Keys stay in configuration.** Never echo a key in the UI, an API response, a log line or an exception.
5. **Writing style.** British English, plain hyphens (no em or en dashes), no full stop at the end of a heading or a label.

Open a pull request against `main`. CI (`Validate`) must pass before merge.
