## Guiding princples
- Optimise for performance while maintaining readability
- Align with idiomatic database design principles.
    - Flag any concerns with proposed design choices if you think they violate idiomatic database design principles

## Code conventions
- DO NOT remove any **existing** comments unless instructed to do so (but do update them if they're outdated)
- DO NOT add any comments unless explicitly instructed to do so
- DO NOT update README files unless explicitly instructed to do so
- Ensure dotnet format suggestions are addressed
- Ensure the architecture diagram in the [README](./README.md) is updated when making changes to the architecture
- Avoid string interpolation in our logs, prefer to use message templates instead, i.e
    - Don't use: `logger.LogInformation($"Health status changed for {address}: {previous} -> {status}");`
    - Do use: `logger.LogInformation("Health status changed for {Address}: {Previous} -> {Status}", address, previous, status);`
- Use primary constructors where possible
- Use collection instantiation where possible

## Tests
- Always ensure tests pass when making changes
- Integration tests are run against a Docker Compose stack. If this stack hasn't started, you can start it using [run.sh](./run.sh)
- Tests use Given When Then format encoded in the method naming
    - i.e. `GivenMultipleKeys_WhenSettingValue_ThenKeysAreDistributedAcrossPartitions`
- Do not add Arrange Act Assert comments, but do logically group statements in these arrangements
- Ensure private helper methods are at the bottom of the file following public methods

## Plans Directory Standards

All files in the `plans/` directory should follow a standardized naming convention and structure.

### Naming Convention
Files should be named as `[type]-[description].md`:
- `plan-`: Proposed or active implementation plans.
- `review-`: Architecture reviews or state-of-the-system documents.
- `story-`: Specific feature stories

Files are moved to `completed/` when finished.

### Standard Templates

#### Plan Template
```markdown
# Plan: [Title]

## Objective
[Brief description of the goal]

## Context & Background
[Context or rationale for the change]

## Architecture & Design
[High-level design details, algorithms, or architectural changes]

## Implementation Steps
[Numbered list of specific tasks]

## Verification & Testing
[How to verify the implementation]

## References
[Links to related documents or issues]
```

#### Review Template
```markdown
# Review: [Title]

## Overview
[High-level summary of the review]

## Current State
[Description of the existing architecture/implementation]

## Gaps & Issues
[Identified problems or missing features]

## Recommendations
[Proposed next steps or improvements]
```

## Contribution guidelines
- You have both `git` and GitHub commandline (`gh`) available to you for creating commits and pull requests
- Use [conventional commit](https://www.conventionalcommits.org/en/v1.0.0/) standard prefixes (`feat`, `test`, `ci`, etc) depending on the type of change being made. These prefixes are written in lowercase.
- Use `feature/` or `bugfix/` branch prefixes depending on the change being made
- Avoid unnecessarily referencing specific commits in PR descriptions
- Do not attribute yourself as author or co-author in commits or PR descrioption