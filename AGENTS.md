## Guiding princples
- Optimise for performance while maintaining readability
- Align with idiomatic database design principles.
    - Flag any concerns with proposed design choices if you think they violate idiomatic database design principles

## Code conventions
- DO NOT add any comments unless explicitly instructed to do so
- DO NOT update README files unless explicitly instructed to do so
- Ensure dotnet format suggestions are addressed
- Ensure the architecture diagram in the [README](./README.md) is updated when making changes to the architecture
- Avoid string interpolation in our logs, prefer to use message templates instead, i.e
    - Don't use: `logger.LogInformation($"Health status changed for {address}: {previous} -> {status}");`
    - Do use: `logger.LogInformation("Health status changed for {Address}: {Previous} -> {Status}", address, previous, status);`
- Use collection instantiation where possible

## Tests
- Always ensure tests pass when making changes
- Integration tests are run against a Docker Compose stack. If this stack hasn't started, you can start it using [run.sh](./run.sh)
- Tests use Given When Then format encoded in the method naming
    - i.e. `GivenMultipleKeys_WhenSettingValue_ThenKeysAreDistributedAcrossPartitions`
- Do not add Arrange Act Assert comments, but do logically group statements in these arrangements
- Ensure private helper methods are at the bottom of the file following public methods

## Contribution guidelines
- You have both `git` and GitHub commandline (`gh`) available to you for creating commits and pull requests
- Use [conventional commit](https://www.conventionalcommits.org/en/v1.0.0/) standard prefixes (`feat`, `test`, `ci`, etc) depending on the type of change being made. These prefixes are written in lowercase.
- Use `feature/` or `bugfix/` branch prefixes depending on the change being made
- Avoid unnecessarily referencing specific commits in PR descriptions
- Do not attribute yourself as author or co-author in commits or PR descrioption