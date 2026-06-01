# Agent & Contributor Guidelines

## Purpose

This document guides contributors and AI agents on how development should proceed for the Sleep Audio Pipeline CDK project. All implementation work must follow the patterns, constraints, and architectural decisions documented here and in the referenced architecture documentation.

## Architecture Source of Truth

The authoritative design document for this project is [docs/ARCHITECTURE.md](ARCHITECTURE.md). This file defines the complete system architecture, component relationships, data flow, security model, and observability strategy.

All implementation issues, code changes, and design decisions **must** align with the architecture as documented in ARCHITECTURE.md. If a proposed change conflicts with the documented architecture, the architecture document must be updated and reviewed **before** the code change is implemented.

## Development Workflow

This project follows a strict TDD-first development approach:

1. **Red** - Write a failing test that describes the desired infrastructure behavior using CDK Assertions (`Amazon.CDK.Assertions` namespace).
2. **Green** - Implement the minimum CDK code required to make the test pass.
3. **Refactor** - Clean up the implementation while keeping all tests green.

Every infrastructure change starts with a test in `src/CdkBase.Tests/`. Tests verify synthesized CloudFormation templates without requiring AWS deployment.

### Testing Pattern

```csharp
// Example CDK Assertions pattern
var app = new App();
var stack = new CdkBaseStack(app, "TestStack");
var template = Template.FromStack(stack);

// Assert resource existence and properties
template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object> { ... });
```

## Issue Implementation Rules

1. **Traceability** - Every implementation issue must trace back to a component defined in [ARCHITECTURE.md](ARCHITECTURE.md). If the component is not documented, add it to the architecture document first.
2. **Consistency** - Code changes must maintain consistency with the documented architecture. Do not introduce components, data flows, or integration patterns that contradict the architecture.
3. **Architecture-First Changes** - Any change that alters the system architecture (new services, changed data flows, modified security boundaries) must update ARCHITECTURE.md **before** implementing the code change.
4. **Incremental Progress** - Implement features in small, testable increments. Each increment should result in a passing test suite.
5. **No Orphan Resources** - Every AWS resource defined in CDK must serve a purpose documented in the architecture. Do not add speculative or unused resources.

## File Organization

| Content Type | Location | Notes |
|---|---|---|
| CDK stack definitions | `src/CdkBase/` | Main infrastructure code |
| CDK app entry point | `src/CdkBase/Program.cs` | App bootstrap and environment config |
| Infrastructure tests | `src/CdkBase.Tests/` | xUnit tests with CDK Assertions |
| Solution file | `src/CdkBase.sln` | .NET solution root |
| Architecture docs | `docs/` | ARCHITECTURE.md and supporting docs |
| CI/CD workflows | `.github/workflows/` | GitHub Actions pipelines |
| CDK configuration | `cdk.json` (root) | CDK CLI settings and context |

### Naming Conventions

- Stack classes: `<Feature>Stack.cs` (e.g., `CdkBaseStack.cs`)
- Test classes: `<Feature>StackTest.cs` (e.g., `CdkBaseStackTest.cs`)
- Documentation: `UPPER_CASE.md` for top-level docs

## Environment Configuration

The project supports multiple deployment environments via CDK context:

- **dev** - Development environment with relaxed constraints for rapid iteration
- **stage** - Staging environment that mirrors production configuration
- **prod** - Production environment with full security, monitoring, and scaling

Environment-specific configuration is passed through CDK context values defined in `cdk.json` and can be overridden at synthesis time:

```bash
cdk synth -c environment=dev
cdk synth -c environment=prod
```

Each environment may vary in:

- Resource naming (environment prefix/suffix)
- Encryption settings (KMS key policies)
- Alarm thresholds and notification targets
- DynamoDB capacity mode (on-demand vs. provisioned)
- S3 lifecycle policies and retention periods
- Log retention duration

Refer to the Multi-Environment Support section in [ARCHITECTURE.md](ARCHITECTURE.md) for detailed configuration differences per environment.
