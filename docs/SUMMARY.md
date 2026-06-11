# Sleep Audio Pipeline - Project Summary

## Overview

The Sleep Audio Pipeline is a fully serverless, event-driven system built on AWS for processing sleep audio recordings. It is defined entirely as Infrastructure as Code using AWS CDK with C# (.NET 8), following a strict Test-Driven Development (TDD) methodology. The project demonstrates how to build production-grade cloud infrastructure incrementally through 12 implementation issues, each validated by automated tests before deployment.

## Key Architectural Decisions

### Why Step Functions for Orchestration

Step Functions was chosen over Lambda-based orchestration (chaining Lambdas via invocations or SQS) because:
- Built-in retry and error handling with exponential backoff at the state level
- Visual execution history for debugging failed pipeline runs
- Native AWS SDK integrations (Polly, DynamoDB) without intermediate Lambda functions
- Clear separation between orchestration logic and processing logic
- State machine definition as data (JSON) is testable via CDK Assertions

### Why Event-Driven Architecture

The event-driven pattern (S3 -> EventBridge -> Step Functions) was chosen over synchronous APIs because:
- Loose coupling between ingestion and processing allows independent scaling
- EventBridge provides content-based filtering without custom routing code
- Asynchronous processing naturally handles variable file sizes and processing times
- Adding new consumers or processing paths requires no changes to existing components
- Fault isolation: a failure in processing does not block new uploads

### Why DynamoDB On-Demand Billing

On-demand (PAY_PER_REQUEST) billing mode was selected because:
- Pipeline traffic is inherently bursty (uploads cluster around user sleep schedules)
- No capacity planning required during development and early production
- Automatically handles spikes without throttling
- Cost-effective for workloads below the provisioned capacity break-even point
- Eliminates the need for auto-scaling configuration and tuning

### Why Python Lambda with C# CDK

The Lambda function is written in Python 3.12 while the CDK infrastructure is in C#:
- Python is the dominant language for AWS Lambda with the richest SDK ecosystem
- boto3 provides idiomatic, well-documented AWS service interactions
- Fast cold starts compared to .NET Lambda (important for on-demand processing)
- C# CDK provides strong typing and IDE support for infrastructure definitions
- Separation of concerns: infrastructure team uses C#, processing logic uses Python
- CDK's `Code.FromAsset` packages the Python code without additional build steps

## What Was Built

### Infrastructure Components (CdkBaseStack)

| Component | Resource | Configuration |
|-----------|----------|---------------|
| Input S3 Bucket | `SleepAudioInputBucket` | KMS encryption, versioned, EventBridge enabled, public access blocked |
| Output S3 Bucket | `SleepAudioOutputBucket` | KMS encryption, versioned, public access blocked |
| DynamoDB Table | `SleepAudioMetadataTable` | On-demand billing, PITR enabled, AWS-managed encryption |
| EventBridge Rule | `AudioUploadRule` | Filters Object Created events from input bucket |
| Step Functions | `SleepAudioPipelineStateMachine` | Full orchestration with logging (ALL), X-Ray tracing |
| Lambda Function | `SleepAudioProcessorFunction` | Python 3.12, 30s timeout, 512MB, X-Ray active |
| SNS Completed Topic | `SleepAudioPipelineCompleted` | KMS encrypted (aws/sns) |
| SNS Failed Topic | `SleepAudioPipelineFailed` | KMS encrypted (aws/sns) |
| CloudWatch Alarms | StateMachine + Lambda | Threshold >= 1, 1-minute period, routes to Failed Topic |
| CloudWatch Dashboard | `SleepAudioPipelineDashboard` | SM executions + Lambda performance graphs |
| CloudWatch Log Group | `StateMachineLogGroup` | 14-day retention, captures execution data |

### State Machine Flow

1. **WriteInitialMetadata** - DynamoDB PutItem (status: PROCESSING)
2. **ValidateInput** - Choice state checking file extension (.mp3/.wav/.ogg/.txt)
3. **ProcessAudio** - Lambda invocation (download, process, upload, update DynamoDB)
4. **SynthesizeSpeech** - Polly AWS SDK integration (placeholder parameters)
5. **UpdateStatusCompleted** - DynamoDB UpdateItem (status: COMPLETED)
6. **PublishSuccessNotification** - SNS publish to Completed topic

Error path: Any task failure -> UpdateStatusFailed (status: FAILED, errorInfo) -> PublishFailureNotification

### Lambda Processing Pipeline

1. Validate event payload (required fields, file extension, bucket name)
2. Pre-flight file size check (HeadObject, max 100 MB)
3. Download input from S3
4. Process content (text preparation for TTS or audio pass-through)
5. Upload processed output to output bucket
6. Update DynamoDB with output location and metadata
7. Return structured response for downstream steps

### CI/CD Pipeline (PipelineStack)

- CDK Pipelines with self-mutation
- GitHub source via CodeStar Connections
- ShellStep synth (dotnet build + cdk synth)
- Application stage deploying CdkBaseStack

### GitHub Actions CI Workflow

- .NET 8 + Node.js 20 setup
- dotnet restore, build, test
- CDK synth for default, dev, and prod environments
- Advisory cdk diff (non-blocking)

## TDD Methodology

### Process

Every feature was implemented following the Red-Green-Refactor cycle:
1. Write failing test(s) describing the expected CloudFormation resources
2. Implement minimum CDK code to make tests pass
3. Refactor while keeping all tests green

### Test Suite

| Test Class | Count | Scope |
|------------|-------|-------|
| CdkBaseStackTest | 70 | Individual resource properties and configuration |
| EndToEndValidationTest | 56 | Full pipeline flow, error paths, retry policies, permissions |
| PipelineStackTest | 3 | CI/CD pipeline configuration |
| Python (test_index.py) | 19 | Lambda handler logic, validation, error handling |
| **Total** | **148** | |

### Testing Patterns Used

- **CDK Assertions**: `Template.FromStack()`, `HasResourceProperties()`, `ResourceCountIs()`
- **JSON state machine inspection**: Serialize StateMachine resources, parse definition, assert state wiring
- **Python unittest with mocking**: `unittest.mock.patch` for boto3 clients, environment variables
- **End-to-end validation**: Verify complete flows through synthesized CloudFormation templates

## Known Limitations

1. **Polly task uses placeholder parameters**: The SynthesizeSpeech step uses static text and voice (Joanna). Dynamic parameters from the S3 event input are not yet wired.

2. **No Bedrock AI enhancement**: The optional AI enhancement step documented in the architecture is not implemented. The pipeline processes audio/text without generative AI augmentation.

3. **No CloudFront delivery**: Processed audio in the output bucket is not served through a CDN. Direct S3 access is required.

4. **Single-region deployment**: No cross-region replication or global table configuration.

5. **Environment-specific configuration is minimal**: While multi-environment support exists via CDK context, the stack does not yet differentiate resource configurations (log retention, alarm thresholds) per environment. All environments use the same defaults.

6. **No S3 lifecycle policies implemented**: The architecture documents lifecycle transitions (IA after 30 days, delete after 90 days) but these are not yet in the CDK code.

7. **JSII resource exhaustion**: Running all 148 C# tests simultaneously can occasionally cause JSII runtime communication errors due to resource pressure. Tests pass reliably when run by class filter.

## Notes for Experiment Report

### Development Approach

- Project was built incrementally across 12 issues, each adding one architectural layer
- Strict TDD ensured every resource was testable before deployment
- Architecture documentation was maintained as the source of truth throughout
- CDK Assertions provided fast feedback (milliseconds) without AWS credentials

### AI-Assisted Development

- AI agents implemented features following documented architecture and guidelines
- Agent Guidelines document (AGENT_GUIDELINES.md) provided consistent development patterns
- Context files tracked findings and environment quirks across implementation sessions
- Feature files with explicit acceptance criteria guided implementation scope

### Metrics

- **Total infrastructure resources**: ~25+ AWS resources defined in CdkBaseStack
- **Test coverage**: 148 automated tests across 4 test suites
- **Languages**: C# (CDK infrastructure), Python (Lambda processing)
- **Security**: KMS encryption on all data stores, least-privilege IAM, public access blocked
- **Observability**: CloudWatch Logs, Alarms, Dashboard, X-Ray tracing

### Key Takeaways

1. TDD for infrastructure provides high confidence in changes without deployment
2. CDK Assertions catch IAM permission, encryption, and configuration issues early
3. Step Functions SDK integrations reduce Lambda count and operational complexity
4. Event-driven patterns naturally separate concerns and enable independent testing
5. Maintaining architecture documentation alongside code prevents drift
