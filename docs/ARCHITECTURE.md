# Sleep Audio Pipeline - Architecture

## Overview

This project defines the AWS infrastructure for a sleep audio pipeline using the AWS Cloud Development Kit (CDK) with C#. The pipeline follows an event-driven architecture to ingest, process, and store sleep audio recordings and their associated metadata.

## Event-Driven Design

The pipeline is designed around loosely-coupled, event-driven components that communicate through AWS managed services. This approach provides:

- **Scalability**: Each component scales independently based on load
- **Resilience**: Failures in one component do not cascade to others
- **Observability**: Events provide a natural audit trail
- **Extensibility**: New consumers can subscribe to events without modifying producers

## Components

### S3 - Audio Ingestion

Amazon S3 serves as the entry point for the pipeline. Audio files are uploaded to an ingestion bucket, which triggers downstream processing through event notifications.

- Receives raw sleep audio recordings
- Supports multiple audio formats
- Provides durable storage with lifecycle policies

### EventBridge - Event Routing

Amazon EventBridge acts as the central event bus, routing events between components based on rules and patterns.

- Receives S3 object creation events
- Routes events to appropriate processing workflows
- Supports filtering and transformation
- Enables fan-out to multiple consumers

### Step Functions - Orchestration

AWS Step Functions orchestrates the multi-step processing workflow for each audio recording.

- Coordinates the sequence of processing steps
- Handles retries and error recovery
- Provides visual workflow monitoring
- Manages parallel processing branches

### Lambda - Processing

AWS Lambda functions perform the actual audio processing and analysis.

- Audio format validation and normalization
- Metadata extraction (duration, sample rate, encoding)
- Audio quality analysis
- Integration with downstream analytics

### DynamoDB - Metadata Store

Amazon DynamoDB stores metadata about processed audio recordings.

- Recording metadata (duration, format, timestamps)
- Processing status and results
- Query patterns optimized for access by user and date
- Single-table design for efficient access

## Data Flow

```
[Audio Upload] --> [S3 Ingestion Bucket]
                        |
                        v
                  [EventBridge]
                        |
                        v
                 [Step Functions]
                   /    |    \
                  v     v     v
             [Lambda] [Lambda] [Lambda]
             (validate) (extract) (analyze)
                  \     |     /
                   v    v    v
                  [DynamoDB]
```

## TDD Approach

This project follows a Test-Driven Development (TDD) methodology using CDK Assertions.

### Red-Green-Refactor Cycle

1. **Red**: Write a failing test that describes the desired infrastructure behavior
2. **Green**: Implement the minimum CDK code to make the test pass
3. **Refactor**: Improve the implementation while keeping tests green

### CDK Assertions

Tests use `Amazon.CDK.Assertions` (included in `Amazon.CDK.Lib`) to verify synthesized CloudFormation templates:

- `Template.FromStack()` - synthesize a stack for testing
- `template.HasResource()` - verify a resource exists with specific properties
- `template.ResourceCountIs()` - verify the count of a resource type
- `template.HasOutput()` - verify stack outputs

### Example Test Pattern

```csharp
[Fact]
public void Stack_HasSqsQueueWithCorrectProperties()
{
    var app = new App();
    var stack = new CdkBaseStack(app, "TestStack");
    var template = Template.FromStack(stack);

    template.HasResourceProperties("AWS::SQS::Queue", new Dictionary<string, object>
    {
        { "VisibilityTimeout", 300 }
    });
}
```

## Future Roadmap

- [ ] S3 ingestion bucket with event notifications
- [ ] EventBridge rules for routing audio processing events
- [ ] Step Functions state machine for orchestration
- [ ] Lambda functions for audio processing
- [ ] DynamoDB table for metadata storage
- [ ] CloudWatch alarms and dashboards
- [ ] IAM roles with least-privilege permissions
- [ ] VPC configuration for network isolation
