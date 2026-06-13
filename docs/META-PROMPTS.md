# Meta-Prompts: Reusable Patterns for Agentic TDD IaC Projects

This document captures reusable patterns extracted from building the Sleep Audio Pipeline using AI-assisted, test-driven infrastructure development. These patterns can be applied to any CDK project where AI agents implement infrastructure incrementally through issues.

## Introduction

Meta-prompting for agentic TDD IaC is the practice of structuring your project documentation, guidelines, and task definitions so that AI agents can reliably implement infrastructure features without ambiguity. The key insight is that the same documentation that makes a project understandable to humans also makes it implementable by agents -- provided it follows specific structural patterns.

The patterns below were validated through 12 implementation issues, producing 148 automated tests and 25+ AWS resources with zero manual CloudFormation editing.

---

## Table of Contents

- [Pattern 1: Agent Instruction Template](#pattern-1-agent-instruction-template)
- [Pattern 2: Issue-Driven Development](#pattern-2-issue-driven-development)
- [Pattern 3: TDD Infrastructure Pattern](#pattern-3-tdd-infrastructure-pattern)
- [Pattern 4: Architecture-First Development](#pattern-4-architecture-first-development)
- [Pattern 5: Context Propagation](#pattern-5-context-propagation)
- [Pattern 6: Error Handling and Observability](#pattern-6-error-handling-and-observability)
- [Reusable Prompt Snippets](#reusable-prompt-snippets)

---

## Pattern 1: Agent Instruction Template

### Purpose

Provide AI agents with a consistent frame of reference for how development should proceed. This prevents drift, enforces conventions, and ensures every implementation session starts from the same baseline.

### Template Structure

An effective agent guidelines document includes:

1. **Architecture Source of Truth** - Link to the authoritative design document
2. **Development Workflow** - Step-by-step TDD process with examples
3. **File Organization** - Where each type of content lives
4. **Naming Conventions** - Consistent patterns for files, classes, and resources
5. **Environment Configuration** - How multi-env support works
6. **CI Workflow** - What the CI pipeline validates
7. **Environment Notes** - Quirks and workarounds

### Reusable Template

```markdown
# Agent & Contributor Guidelines

## Architecture Source of Truth

The authoritative design document is [docs/ARCHITECTURE.md](ARCHITECTURE.md).
All implementation must align with documented architecture.
If a proposed change conflicts, update the architecture document FIRST.

## Development Workflow

This project follows strict TDD:
1. **Red** - Write a failing test describing desired behavior
2. **Green** - Implement minimum code to pass
3. **Refactor** - Clean up while keeping tests green

## Testing Pattern

[Include language-specific testing examples here]

## File Organization

| Content Type | Location | Notes |
|---|---|---|
| Infrastructure code | `src/<ProjectName>/` | Main CDK stacks |
| Tests | `src/<ProjectName>.Tests/` | xUnit/Jest/pytest tests |
| Documentation | `docs/` | Architecture, guidelines, summary |
| CI/CD | `.github/workflows/` | GitHub Actions |

## Naming Conventions

- Stack classes: `<Feature>Stack.cs`
- Test classes: `<Feature>StackTest.cs`
- Documentation: `UPPER_CASE.md` for top-level docs

## CI Workflow

[Describe what CI validates and in what order]

## Environment Notes

[Document any quirks, workarounds, or environment-specific issues]
```

### Why This Works

Agents perform best when they have explicit boundaries. Without guidelines, an agent might:
- Create resources not in the architecture
- Skip tests
- Use inconsistent naming
- Place files in unexpected locations

The template eliminates these failure modes by front-loading decisions.

---

## Pattern 2: Issue-Driven Development

### Purpose

Structure implementation work into discrete, ordered issues that an agent can pick up and complete independently. Each issue should be self-contained, with clear inputs, outputs, and success criteria.

### Issue Structure Template

```markdown
# Issue Title: [Action Verb] + [Component] + [Outcome]

## Context
[Why this issue exists and what it builds on]

## Acceptance Criteria
- [ ] Resource X exists with properties Y
- [ ] Test class Z contains N passing tests
- [ ] CDK synth produces valid CloudFormation
- [ ] All existing tests still pass

## Implementation Steps
1. Write failing tests for [specific behavior]
2. Implement [specific CDK construct]
3. Wire [component A] to [component B]
4. Verify with `cdk synth`

## Dependencies
- Requires: Issue #N (component that must exist first)
- Blocked by: Nothing / Issue #M

## Success Metrics
- Test count increases by N
- `cdk synth` produces template with resource type X
- No existing tests broken
```

### Ordering Principles

Issues should be ordered to minimize cross-cutting changes:

1. **Foundation first** - S3 buckets, DynamoDB tables, base constructs
2. **Orchestration second** - Step Functions, EventBridge rules
3. **Compute third** - Lambda functions, SDK integrations
4. **Observability fourth** - Alarms, dashboards, logging
5. **Pipeline last** - CI/CD, deployment automation

### Example from This Project

The Sleep Audio Pipeline was built across 12 issues in this order:

1. Bootstrap CDK project with TDD scaffolding
2. S3 input/output buckets with encryption
3. DynamoDB metadata table
4. EventBridge rule for S3 events
5. Step Functions state machine skeleton
6. Lambda function with IAM permissions
7. State machine wiring (Lambda + Polly + DynamoDB tasks)
8. SNS notification topics
9. CloudWatch alarms and dashboard
10. Error handling and retry policies
11. End-to-end validation tests
12. CI/CD pipeline and documentation

Each issue produced a working, tested increment that could be deployed independently.

---

## Pattern 3: TDD Infrastructure Pattern

### Purpose

Apply Red-Green-Refactor to CDK infrastructure development using CDK Assertions for fast, deployment-free validation.

### The Cycle

```
[Write Failing Test] --> [Implement CDK Code] --> [Tests Pass] --> [Refactor] --> [Next Test]
        RED                    GREEN                                REFACTOR
```

### C# CDK Assertions Examples

#### Testing Resource Existence

```csharp
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

public class MyStackTest
{
    private readonly Template _template;

    public MyStackTest()
    {
        var app = new App();
        var stack = new MyStack(app, "TestStack");
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void Stack_Creates_S3_Bucket_With_Encryption()
    {
        _template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["BucketEncryption"] = new Dictionary<string, object>
            {
                ["ServerSideEncryptionConfiguration"] = Match.AnyValue()
            }
        });
    }

    [Fact]
    public void Stack_Creates_Exactly_Two_SNS_Topics()
    {
        _template.ResourceCountIs("AWS::SNS::Topic", 2);
    }
}
```

#### Testing IAM Permissions

```csharp
[Fact]
public void Lambda_Has_DynamoDB_ReadWrite_Permissions()
{
    _template.HasResourceProperties("AWS::IAM::Policy", new Dictionary<string, object>
    {
        ["PolicyDocument"] = new Dictionary<string, object>
        {
            ["Statement"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Action"] = Match.ArrayWith(new object[]
                    {
                        "dynamodb:PutItem",
                        "dynamodb:GetItem",
                        "dynamodb:UpdateItem"
                    }),
                    ["Effect"] = "Allow"
                })
            })
        }
    });
}
```

#### Testing Step Functions State Machine Definition

```csharp
[Fact]
public void StateMachine_Has_Correct_State_Wiring()
{
    var resources = _template.FindResources("AWS::StepFunctions::StateMachine");

    foreach (var resource in resources)
    {
        var properties = resource.Value as Dictionary<string, object>;
        var definitionString = properties?["Properties"]
            as Dictionary<string, object>;

        // Parse the state machine definition and assert states
        // Verify: StartAt, States, transitions, error handling
    }
}
```

#### Testing Alarm Configuration

```csharp
[Fact]
public void StateMachine_Failure_Alarm_Routes_To_SNS_Failed_Topic()
{
    _template.HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
    {
        ["MetricName"] = "ExecutionsFailed",
        ["Namespace"] = "AWS/States",
        ["ComparisonOperator"] = "GreaterThanOrEqualToThreshold",
        ["Threshold"] = 1,
        ["AlarmActions"] = Match.AnyValue()
    });
}
```

### Key Principles

1. **Test the template, not the deployment** - CDK Assertions work on synthesized CloudFormation
2. **One assertion per test** - Makes failures precise and actionable
3. **Share fixture setup** - Use constructor or class-level template synthesis
4. **Test properties, not names** - Resource names are implementation details
5. **Validate wiring** - Ensure resources reference each other correctly (Refs, GetAtts)

---

## Pattern 4: Architecture-First Development

### Purpose

Maintain architecture documentation as the authoritative source of truth. Code implements the architecture; the architecture does not retroactively describe the code.

### Process

```
[Define Architecture] --> [Write Issues from Architecture] --> [Implement with TDD] --> [Update Architecture if needed]
```

### Architecture Document Template

```markdown
# System Architecture

## High-Level Overview
[2-3 paragraphs explaining what the system does and why]

## Architecture Diagram
[Mermaid or image showing component relationships]

## Component Specifications

### Component Name
- **Service**: AWS service used
- **Purpose**: Why this component exists
- **Configuration**: Key settings (encryption, capacity, retention)
- **Inputs**: What triggers or feeds this component
- **Outputs**: What this component produces
- **Error Handling**: How failures are managed

## Data Flow
[Step-by-step description of how data moves through the system]

## Security Model
[Encryption, IAM, network boundaries]

## Observability
[Logging, metrics, alarms, tracing]

## Multi-Environment Support
[How configuration varies across dev/staging/prod]
```

### Rules for Architecture-First Development

1. **No undocumented resources** - Every AWS resource in CDK must appear in the architecture doc
2. **Architecture changes require review** - Update the doc before implementing code changes
3. **Diagrams stay current** - If the data flow changes, the Mermaid diagram must update
4. **Issues reference architecture** - Each implementation issue should cite the architecture section it implements

### Benefits for Agent Development

- Agents have a single source of truth to consult
- Reduces hallucination (agents implement what is documented, not what they invent)
- Makes code review straightforward (does the code match the architecture?)
- Creates natural checkpoints (architecture review before implementation)

---

## Pattern 5: Context Propagation

### Purpose

Maintain state across agent sessions using structured context files. Agents do not have persistent memory, so the project must encode all relevant context in files that persist between sessions.

### File Structure

```
.agents/tasks/<task-id>/
  context.json          # Project-wide context (build commands, patterns, constraints)
  task.json             # Task metadata (status, description)
  features/
    FEAT-001.json       # Feature spec with steps, acceptance criteria, status
    FEAT-002.json       # ...
  <date>-review.md      # Post-implementation review notes
```

### context.json Template

```json
{
  "project_type": "AWS CDK C# serverless infrastructure project",
  "language": "C# (CDK), Python (Lambda)",
  "build_system": "dotnet CLI, CDK CLI via npx",
  "test_framework": "xUnit with CDK Assertions, Python unittest",
  "build_command": "dotnet build src/Solution.sln",
  "test_command": "dotnet test src/Solution.sln --verbosity quiet",
  "verification_instructions": "1. restore 2. build 3. test 4. cdk synth",
  "environment_constraints": "Must unset NODE_OPTIONS before commands",
  "key_patterns": "TDD-first, architecture as source of truth",
  "relevant_files": ["README.md", "docs/ARCHITECTURE.md"],
  "directory_structure": "Brief description of project layout"
}
```

### Feature File Template

```json
{
  "id": "FEAT-001",
  "type": "infrastructure",
  "description": "What this feature implements",
  "status": "pending|in_progress|completed|blocked",
  "steps": [
    "Step 1: Write tests for X",
    "Step 2: Implement Y",
    "Step 3: Verify Z"
  ],
  "acceptance_criteria": [
    "Resource X exists with property Y",
    "N tests pass",
    "Build succeeds"
  ],
  "verification": [
    "dotnet build src/Solution.sln",
    "dotnet test src/Solution.sln"
  ],
  "findings": "Any discoveries made during implementation"
}
```

### Why Context Propagation Matters

Without context files:
- Each session re-discovers the build command
- Environment quirks are rediscovered (wasting time)
- Previous decisions are forgotten
- Test counts are unknown (cannot verify nothing regressed)

With context files:
- Agent starts immediately with correct commands
- Known quirks are pre-loaded (e.g., "unset NODE_OPTIONS")
- Findings from previous sessions inform current work
- Acceptance criteria provide clear completion signals

---

## Pattern 6: Error Handling and Observability

### Purpose

Define consistent error handling and observability patterns for Step Functions workflows with CDK, ensuring every failure path is tested and monitored.

### Step Functions Error Handling in CDK (C#)

#### Retry Configuration Pattern

```csharp
// In state machine definition JSON (built via CDK):
// Every task state should have retry configuration

var processAudioTask = new LambdaInvoke(this, "ProcessAudio", new LambdaInvokeProps
{
    LambdaFunction = processorFunction,
    RetryOnServiceExceptions = false  // We define custom retries
});

// Add specific retry for transient errors
processAudioTask.AddRetry(new RetryProps
{
    Errors = new[] { "Lambda.ServiceException", "Lambda.SdkClientException" },
    Interval = Duration.Seconds(2),
    MaxAttempts = 3,
    BackoffRate = 2.0
});

// Add catch-all for unrecoverable errors
processAudioTask.AddCatch(errorHandler, new CatchProps
{
    Errors = new[] { "States.ALL" },
    ResultPath = "$.errorInfo"
});
```

#### Testing Retry Policies

```csharp
[Fact]
public void ProcessAudio_Has_Retry_For_Lambda_Exceptions()
{
    var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");

    foreach (var sm in stateMachines)
    {
        var definition = GetDefinitionString(sm);
        var processAudioState = definition["States"]["ProcessAudio"];
        var retries = processAudioState["Retry"] as List<object>;

        Assert.NotNull(retries);
        Assert.Contains(retries, r =>
        {
            var retry = r as Dictionary<string, object>;
            var errors = retry?["ErrorEquals"] as List<object>;
            return errors?.Contains("Lambda.ServiceException") == true;
        });
    }
}
```

### CloudWatch Alarms Pattern

```csharp
// Alarm for state machine failures
var executionFailedAlarm = new Alarm(this, "StateMachineFailedAlarm", new AlarmProps
{
    Metric = stateMachine.MetricFailed(),
    Threshold = 1,
    EvaluationPeriods = 1,
    ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
    TreatMissingData = TreatMissingData.NOT_BREACHING
});

// Route alarm to SNS
executionFailedAlarm.AddAlarmAction(new SnsAction(failedTopic));

// Alarm for Lambda errors
var lambdaErrorAlarm = new Alarm(this, "LambdaErrorAlarm", new AlarmProps
{
    Metric = processorFunction.MetricErrors(),
    Threshold = 1,
    EvaluationPeriods = 1,
    ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
    TreatMissingData = TreatMissingData.NOT_BREACHING
});

lambdaErrorAlarm.AddAlarmAction(new SnsAction(failedTopic));
```

### Testing Alarm Wiring

```csharp
[Fact]
public void Alarms_Have_Actions_Pointing_To_Failed_Topic()
{
    _template.HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
    {
        ["AlarmActions"] = Match.AnyValue(),
        ["ComparisonOperator"] = "GreaterThanOrEqualToThreshold",
        ["Threshold"] = 1
    });
}
```

### CloudWatch Dashboard Pattern

```csharp
var dashboard = new Dashboard(this, "PipelineDashboard", new DashboardProps
{
    DashboardName = "SleepAudioPipelineDashboard"
});

dashboard.AddWidgets(
    new GraphWidget(new GraphWidgetProps
    {
        Title = "State Machine Executions",
        Left = new IMetric[]
        {
            stateMachine.MetricStarted(),
            stateMachine.MetricSucceeded(),
            stateMachine.MetricFailed()
        }
    }),
    new GraphWidget(new GraphWidgetProps
    {
        Title = "Lambda Performance",
        Left = new IMetric[]
        {
            processorFunction.MetricInvocations(),
            processorFunction.MetricErrors(),
            processorFunction.MetricDuration()
        }
    })
);
```

### Observability Checklist

For any Step Functions pipeline, ensure:

- [ ] State machine has CloudWatch Logs enabled (log level ALL)
- [ ] X-Ray tracing is active on state machine and Lambda
- [ ] Every task state has retry configuration for transient errors
- [ ] Every task state has a catch block routing to error handling
- [ ] CloudWatch Alarm exists for `ExecutionsFailed >= 1`
- [ ] CloudWatch Alarm exists for Lambda `Errors >= 1`
- [ ] All alarms route to an SNS topic
- [ ] Dashboard shows key execution and performance metrics
- [ ] Log retention is configured (not indefinite)
- [ ] Error path updates metadata with failure details

---

## Reusable Prompt Snippets

The following snippets can be copied directly into agent instructions or issue descriptions for new projects.

### Snippet: TDD Constraint Block

```
## Development Constraints

- ALL infrastructure changes MUST start with a failing test
- Tests use CDK Assertions (Template.FromStack, HasResourceProperties, ResourceCountIs)
- No resource may be added without a corresponding test
- Tests must pass WITHOUT deploying to AWS
- Run `dotnet test` after every change to verify no regressions
```

### Snippet: Architecture Alignment Check

```
## Before Implementation

1. Read docs/ARCHITECTURE.md for the target architecture
2. Identify which component(s) this issue implements
3. Verify the component exists in the architecture diagram
4. If the component is missing from the architecture, STOP and update the doc first
5. Only after architecture alignment is confirmed, begin writing tests
```

### Snippet: CDK Synth Verification

```
## Verification Steps

After all tests pass:
1. `npx cdk synth` - Verify default environment synthesizes
2. `npx cdk synth -c environment=dev` - Verify dev config
3. `npx cdk synth -c environment=prod` - Verify prod config
4. Review synthesized template for unexpected resources
5. Ensure no CDK warnings or errors in output
```

### Snippet: Issue Completion Criteria

```
## Definition of Done

- [ ] All acceptance criteria met
- [ ] New tests written and passing
- [ ] All existing tests still passing (no regressions)
- [ ] CDK synth succeeds for all environments
- [ ] Code follows project naming conventions
- [ ] No orphan resources (every resource serves documented purpose)
- [ ] Changes committed with conventional commit message
```

### Snippet: Environment Safety

```
## Environment Notes

- Run `unset NODE_OPTIONS` before dotnet or CDK commands
- Project targets net8.0 but runs on .NET 9 via RollForward
- JSII tests may fail under resource pressure; use --filter for isolation
- Python 3.12 required for Lambda tests
- CDK CLI available via `npx cdk` (no global install required)
```

### Snippet: Context File Bootstrap

```
## First Session Setup

Before starting implementation:
1. Read `.agents/tasks/<task-id>/context.json` for build commands and constraints
2. Read the feature file for acceptance criteria
3. Check findings from previously completed features
4. Run `git status` to understand workspace state
5. Run the build command to verify baseline
6. Run the test command to verify all tests pass
7. Only then begin implementing
```

---

## Applying These Patterns to New Projects

To bootstrap a new agentic TDD IaC project using these patterns:

1. **Create `docs/ARCHITECTURE.md`** - Define your target architecture before writing any code
2. **Create `docs/AGENT_GUIDELINES.md`** - Use Pattern 1 to structure your guidelines
3. **Define issues using Pattern 2** - Break the architecture into ordered, testable increments
4. **Set up context propagation (Pattern 5)** - Create the `.agents/tasks/` structure
5. **Write your first test** - Use Pattern 3 to establish the TDD rhythm
6. **Add observability from the start (Pattern 6)** - Do not defer error handling to "later"

The result is a project where any AI agent (or human contributor) can pick up an issue and implement it correctly on the first attempt, with clear verification that the implementation matches the documented architecture.
