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

### CDK Testing Pattern (C#)

```csharp
// Example CDK Assertions pattern
var app = new App();
var stack = new CdkBaseStack(app, "TestStack");
var template = Template.FromStack(stack);

// Assert resource existence and properties
template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object> { ... });

// Assert resource count
template.ResourceCountIs("AWS::SNS::Topic", 2);

// Inspect state machine definition (JSON serialization)
var resources = template.FindResources("AWS::StepFunctions::StateMachine");
// Parse DefinitionString and assert state wiring, error handling, etc.
```

### Lambda Testing Pattern (Python)

Lambda tests use Python's `unittest` framework with `unittest.mock` for AWS service mocking:

```python
import unittest
from unittest.mock import patch, MagicMock
import json

class TestHandler(unittest.TestCase):
    @patch.dict("os.environ", {
        "TABLE_NAME": "test-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket"
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_handler_valid_input(self, mock_dynamodb, mock_s3):
        # Arrange: set up mock responses
        mock_s3.head_object.return_value = {"ContentLength": 1024}
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: b"test content"),
            "ContentType": "audio/mp3",
            "ContentLength": 1024
        }

        event = {
            "detail": {
                "bucket": {"name": "test-input-bucket"},
                "object": {"key": "test.mp3"}
            }
        }

        # Act
        from index import handler
        result = handler(event, MagicMock(aws_request_id="test-123"))

        # Assert
        self.assertEqual(result["statusCode"], 200)
        self.assertEqual(result["body"]["status"], "PROCESSED")
```

Run Python tests with:
```bash
cd src/CdkBase/lambda/process_audio
python -m pytest test_index.py -v
# or
python -m unittest test_index -v
```

### End-to-End Validation Test Approach

The `EndToEndValidationTest` class validates complete pipeline flows through the synthesized CloudFormation template. It does not deploy to AWS but verifies:

- **Happy path**: State machine definition correctly wires WriteInitialMetadata -> ValidateInput -> ProcessAudio -> SynthesizeSpeech -> UpdateStatusCompleted -> PublishSuccessNotification
- **Error paths**: Invalid file extensions route through ValidationFailed -> UpdateStatusFailed -> PublishFailureNotification
- **Retry policies**: All task states have correct retry configuration (intervals, max attempts, backoff rates)
- **Lambda permissions**: Function has correct IAM grants (DynamoDB read/write, S3 read on input, S3 write on output)
- **CloudWatch alarm wiring**: Alarms have alarm actions referencing the SNS Failed Topic
- **Specific error catches**: ProcessAudio has Lambda.ServiceException and Lambda.SdkClientException catches before States.ALL

## Issue Implementation Rules

1. **Traceability** - Every implementation issue must trace back to a component defined in [ARCHITECTURE.md](ARCHITECTURE.md). If the component is not documented, add it to the architecture document first.
2. **Consistency** - Code changes must maintain consistency with the documented architecture. Do not introduce components, data flows, or integration patterns that contradict the architecture.
3. **Architecture-First Changes** - Any change that alters the system architecture (new services, changed data flows, modified security boundaries) must update ARCHITECTURE.md **before** implementing the code change.
4. **Incremental Progress** - Implement features in small, testable increments. Each increment should result in a passing test suite.
5. **No Orphan Resources** - Every AWS resource defined in CDK must serve a purpose documented in the architecture. Do not add speculative or unused resources.

## File Organization

| Content Type | Location | Notes |
|---|---|---|
| CDK stack definitions | `src/CdkBase/` | Main infrastructure code (CdkBaseStack.cs, PipelineStack.cs) |
| CDK app entry point | `src/CdkBase/Program.cs` | App bootstrap and environment config |
| Lambda handler | `src/CdkBase/lambda/process_audio/index.py` | Python 3.12 audio processor |
| Lambda tests | `src/CdkBase/lambda/process_audio/test_index.py` | Python unittest with mocking |
| Infrastructure tests | `src/CdkBase.Tests/` | xUnit tests with CDK Assertions |
| End-to-end validation | `src/CdkBase.Tests/EndToEndValidationTest.cs` | Full pipeline flow tests |
| Pipeline tests | `src/CdkBase.Tests/PipelineStackTest.cs` | CI/CD pipeline tests |
| Solution file | `src/CdkBase.sln` | .NET solution root |
| Architecture docs | `docs/` | ARCHITECTURE.md, SUMMARY.md, AGENT_GUIDELINES.md |
| CI/CD workflows | `.github/workflows/ci.yml` | GitHub Actions pipeline |
| CDK configuration | `cdk.json` (root) | CDK CLI settings and context flags |

### Naming Conventions

- Stack classes: `<Feature>Stack.cs` (e.g., `CdkBaseStack.cs`, `PipelineStack.cs`)
- Test classes: `<Feature>StackTest.cs` or `<Feature>Test.cs` (e.g., `CdkBaseStackTest.cs`, `EndToEndValidationTest.cs`)
- Lambda handlers: `index.py` with a `handler` function entry point
- Lambda tests: `test_index.py` following Python unittest conventions
- Documentation: `UPPER_CASE.md` for top-level docs

## Environment Configuration

The project supports multiple deployment environments via CDK context:

- **dev** - Development environment (default when no environment specified)
- **staging** - Staging environment that mirrors production configuration
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

## CI Workflow

The GitHub Actions CI workflow (`.github/workflows/ci.yml`) runs on every push to `main` and on all pull requests:

1. **Setup**: .NET 8, Node.js 20, CDK CLI
2. **Restore**: `dotnet restore src/CdkBase.sln`
3. **Build**: `dotnet build src/CdkBase.sln --no-restore`
4. **Test**: `dotnet test src/CdkBase.sln --no-build --verbosity normal`
5. **CDK Synth**: Default, dev, and prod environments
6. **CDK Diff**: Advisory only (non-blocking, requires AWS credentials)

All steps except CDK Diff must pass for the workflow to succeed.

## Environment Notes

- **NODE_OPTIONS**: Must be unset (`unset NODE_OPTIONS`) before running any `dotnet test`, `npx`, or `cdk` commands in certain sandbox environments
- **Python version**: Lambda tests require Python 3.12 (use `PYENV_VERSION=3.12.x` if multiple versions are available)
- **.NET version**: Project targets net8.0 but works with .NET 9 via `RollForward=Major` in the project file
- **JSII resource exhaustion**: Running all 148 C# tests simultaneously may occasionally fail. Use `--filter` to run test classes individually if needed
