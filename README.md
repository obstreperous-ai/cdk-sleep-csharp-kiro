# Sleep Audio Pipeline - CDK Infrastructure

AWS CDK infrastructure (C#) for a sleep audio processing pipeline. This project defines cloud resources for ingesting, processing, and storing sleep audio recordings using an event-driven architecture.

## Project Purpose

This repository contains the Infrastructure-as-Code (IaC) for the sleep audio pipeline, built with AWS CDK in C#. The pipeline processes sleep audio recordings through an event-driven architecture using S3, EventBridge, Step Functions, Lambda, and DynamoDB.

## TDD-First Development

This project follows a Test-Driven Development approach:

1. **Write a failing test** describing the infrastructure you want
2. **Implement the CDK code** to make the test pass
3. **Refactor** while keeping tests green

All infrastructure changes start with a test in `src/CdkBase.Tests/`.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- [AWS CDK CLI](https://docs.aws.amazon.com/cdk/v2/guide/cli.html) (`npm install -g aws-cdk`)

## Getting Started

```bash
# Restore NuGet packages
dotnet restore src/CdkBase.sln

# Build the solution
dotnet build src/CdkBase.sln

# Run tests
dotnet test src/CdkBase.sln

# Synthesize CloudFormation template
cdk synth

# Compare with deployed stack
cdk diff
```

## Running Tests

```bash
# Run all tests with verbose output
dotnet test src/CdkBase.sln --verbosity normal

# Run tests with a filter
dotnet test src/CdkBase.sln --filter "FullyQualifiedName~CdkBaseStackTest"
```

Tests use CDK Assertions (`Amazon.CDK.Assertions` namespace from `Amazon.CDK.Lib`) to verify synthesized CloudFormation templates without deploying.

## Project Structure

```
.
├── src/
│   ├── CdkBase.sln              # Solution file
│   ├── CdkBase/                 # Main CDK app
│   │   ├── Program.cs           # CDK app entry point
│   │   ├── CdkBaseStack.cs      # Stack definition
│   │   └── CdkBase.csproj       # Project file
│   └── CdkBase.Tests/           # xUnit test project
│       ├── CdkBaseStackTest.cs   # Stack tests (TDD-first)
│       └── CdkBase.Tests.csproj  # Test project file
├── docs/
│   └── ARCHITECTURE.md          # Architecture documentation
├── .github/
│   └── workflows/
│       └── ci.yml               # CI pipeline
├── cdk.json                     # CDK configuration
└── README.md
```

## Architecture Overview

The sleep audio pipeline uses an event-driven architecture:

```
[Audio Upload] --> [S3] --> [EventBridge] --> [Step Functions] --> [Lambda] --> [DynamoDB]
```

- **S3**: Audio file ingestion and storage
- **EventBridge**: Event routing and filtering
- **Step Functions**: Workflow orchestration
- **Lambda**: Audio processing functions
- **DynamoDB**: Metadata storage

For detailed architecture documentation, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Useful CDK Commands

| Command | Description |
|---------|-------------|
| `dotnet build src/CdkBase.sln` | Compile the CDK app |
| `cdk synth` | Emit the synthesized CloudFormation template |
| `cdk diff` | Compare deployed stack with current state |
| `cdk deploy` | Deploy this stack to your AWS account/region |
| `dotnet test src/CdkBase.sln` | Run infrastructure tests |
