## Guiding princples
- Optimise for performance while maintaining readability

## Code conventions
- DO NOT add any comments unless explicitly instructed to do so
- DO NOT update README files unless explicitly instructed to do so

## Tests
- Always ensure tests pass when making changes
- Tests use Given When Then format encoded in the method naming
    - i.e. `GivenMultipleKeys_WhenSettingValue_ThenKeysAreDistributedAcrossPartitions`
- Do not add Arrange Act Assert comments, but do logically group statements in these arrangements
- Ensure private helper methods are at the bottom of the file following public methods