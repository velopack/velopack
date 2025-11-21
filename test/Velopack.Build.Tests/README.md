# Velopack.Build.Tests

This project contains unit and integration tests for the Velopack.Build MSBuild tasks package.

## Test Coverage

### Unit Tests

#### ArgumentBuilderTests
Tests the `ArgumentBuilder` class which constructs command-line arguments for VPK tool invocation:
- Command and option addition
- Value quoting for paths with spaces
- Boolean flag handling
- Integer options with defaults
- Complex argument scenarios

#### VpkToolConfigurationTests
Tests the `VpkToolConfiguration` and `ResolvedTool` classes:
- Default configuration values
- Property setters
- Tool mode enumeration
- Execution prefix generation

#### PackTaskTests
Tests the `PackTask` MSBuild task:
- Required property validation
- Default value verification
- Tool mode parsing
- Argument building for various scenarios
- Boolean flag handling
- Signing parameter handling

#### PublishTaskTests
Tests the `PublishTask` MSBuild task:
- Required property validation
- Default values
- Argument building for publish command
- Channel and WaitForLive flag handling
- ServiceUrl and ApiKey property handling

### Integration Tests

#### VpkToolResolverIntegrationTests
Integration tests for actual VPK tool resolution and installation (marked as Skip by default):
- Tool resolution with Auto mode
- Skip install behavior
- Specific version resolution
- Tool installation detection

**Note:** Integration tests are skipped by default as they require:
- .NET SDK with dotnet CLI
- Network access to NuGet.org
- Permissions to install dotnet global/local tools

To run integration tests, remove the `Skip` attribute from the test methods.

## Running Tests

### Run All Tests (Unit Tests Only)
```bash
dotnet test
```

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~ArgumentBuilderTests"
```

### Run Integration Tests
```bash
# Remove Skip attributes from integration tests first, then:
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

## Test Structure

- **GlobalUsings.cs** - Common using directives
- **MockBuildEngine.cs** - Mock IBuildEngine for testing MSBuild tasks
- **TestHelper.cs** - Helper utilities for creating test environments

## Dependencies

- **xUnit** - Test framework
- **Moq** - Mocking framework (for future use)
- **Microsoft.Build.Utilities.Core** - For MSBuild task testing
- **Microsoft.NET.Test.Sdk** - Test SDK

## Best Practices

1. **Isolation** - Each test should be independent and not rely on external state
2. **Cleanup** - Use `TestHelper.CleanupTempDirectory()` in test disposal
3. **Skip Integration Tests** - Keep integration tests skipped in CI unless specifically enabled
4. **Mock External Dependencies** - Use mocks for dotnet CLI calls in unit tests
5. **Clear Test Names** - Use descriptive test method names following pattern: `MethodName_Scenario_ExpectedResult`

## Future Improvements

- [ ] Add tests for DotNetToolRunner process execution
- [ ] Add tests for VpkToolResolver version extraction
- [ ] Mock process execution for better unit test isolation
- [ ] Add performance tests for tool resolution
- [ ] Add tests for MSBuild integration (.targets file)
- [ ] Add end-to-end tests with actual dotnet publish
