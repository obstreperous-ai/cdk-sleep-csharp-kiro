# Experiment Design: Agentic TDD Infrastructure-as-Code

## Overview & Goals

This repository is a **TDD Infrastructure-as-Code experiment** exploring whether production-grade serverless infrastructure can be constructed entirely through strict issue-driven development with AI-assisted agents.

### Experiment Matrix

This project is part of a broader experiment matrix: **5 programming languages x 3 AI assistants** (15 repositories total). The goal is to evaluate how different AI agents perform when given identical infrastructure challenges across multiple language ecosystems.

- **This repository:** C# (.NET 8) with AWS CDK, driven by **Kiro**
- **Organization:** [obstreperous-ai](https://github.com/obstreperous-ai)
- **Target:** Build a fully functional event-driven sleep audio processing pipeline from zero to production-ready through pure TDD, with architecture documentation as the source of truth

### Primary Goals

1. Demonstrate that production-quality serverless infrastructure can be built entirely through AI-assisted, issue-driven TDD
2. Validate that architecture-first documentation prevents implementation drift
3. Measure the effectiveness of strict Red-Green-Refactor discipline for infrastructure code
4. Create reusable patterns for agentic TDD IaC development

---

## Experiment Matrix & Actors

### Languages & Agents

The broader experiment uses 5 languages and 3 AI agents. Each agent receives the same set of issues (adapted for language idioms) to enable cross-comparison of:

- Code quality and test coverage
- Adherence to TDD discipline
- Architecture interpretation accuracy
- Error handling completeness
- Documentation quality

### This Repository's Actor: Kiro

- **Agent:** Kiro (AI-assisted development agent)
- **Role:** Autonomous implementation of all features from structured GitHub issues
- **Repository owner** (obstreperous-ai) created issues with explicit acceptance criteria
- **Kiro** implemented them autonomously, creating PRs that were reviewed and merged
- **Development model:** Issue created -> Kiro implements -> PR opened -> Review -> Merge

### Interaction Model

The experiment follows a strict separation of concerns:

- **Human (obstreperous-ai):** Writes issues, reviews PRs, provides architectural guidance
- **Agent (Kiro):** Implements features, writes tests, creates documentation, opens PRs
- **No hand-holding:** Issues contain all necessary context; the agent works independently

---

## Methodology

### Strict TDD (Red-Green-Refactor)

Every infrastructure resource follows the Red-Green-Refactor cycle:

1. **Red** - Write failing tests describing the expected CloudFormation resources using CDK Assertions (`Template.FromStack`, `HasResourceProperties`, `ResourceCountIs`)
2. **Green** - Implement the minimum CDK code to make the tests pass
3. **Refactor** - Clean up implementation while keeping all tests green

This provides millisecond feedback without AWS deployment. Tests validate:

- Resource existence and count
- Property configuration (encryption, retention, permissions)
- IAM policy correctness (least-privilege)
- Cross-resource references and wiring
- Error handling paths

**Final test count:** 148 total tests (129 C# infrastructure + 19 Python Lambda unit tests)

### Issue-Driven Development

The entire pipeline was built incrementally across **12 implementation issues** (plus 2 documentation issues):

- Issues were numbered [1] through [14] with explicit ordering and dependencies
- Each issue produced a working, tested increment that could be deployed independently
- Issues were structured with: Goal, Strict TDD Rules, Specific Requirements, Tasks (in strict order), Success Criteria, and "Next Issue" pointers
- No issue was started until its predecessor was merged

### Architecture-as-Code

- `ARCHITECTURE.md` was written BEFORE any implementation code (Issue [2])
- The architecture document served as the **authoritative source of truth** throughout development
- All implementation issues traced back to components in the architecture
- Mermaid diagrams were maintained in sync with code throughout
- Implementation divergence from architecture required explicit documentation

---

## Issue History & Development Timeline

The table below summarizes all 14 issues in chronological order:

| # | Issue | Title | PR | Merged | Key Deliverable | Tests |
|---|-------|-------|-----|--------|-----------------|-------|
| 1 | #1 | Bootstrap: C# CDK + Strict TDD + Agent Config | #2 | 2026-05-28 | xUnit test project, CI workflow, architecture skeleton | 2 |
| 2 | #3 | Architecture Design (Mermaid + Detailed Spec) | #4 | 2026-06-02 | Comprehensive ARCHITECTURE.md, AGENT_GUIDELINES.md | 0 (docs only) |
| 3 | #5 | Core S3 Buckets + EventBridge Rule | #6 | 2026-06-03 | Input/Output S3, EventBridge rule, stub SQS target | 12 |
| 4 | #7 | Step Functions State Machine + Polly | #8 | 2026-06-04 | State machine, Polly SDK integration, EventBridge wiring | 15 (total) |
| 5 | #9 | DynamoDB Metadata Table + State Machine I/O | #10 | 2026-06-05 | Metadata table, DynamoDB tasks in state machine | 24 (total) |
| 6 | #11 | SNS Notifications + Error Handling | #12 | 2026-06-06 | 2 SNS topics, error paths, status updates | 31 (total) |
| 7 | #13 | Lambda Function Skeleton + State Machine Integration | #14 | 2026-06-07 | Python 3.12 Lambda, LambdaInvoke task, DynamoDB access | 37 (total) |
| 8 | #15 | Complete Pipeline Wiring + Input Validation | #16 | 2026-06-08 | ValidateInput Choice state, Lambda validation, end-to-end flow | 45 (total) |
| 9 | #17 | Pipeline Testing + Multi-Environment + CDK Pipelines | #18 | 2026-06-09 | PipelineStack, env support, expanded test coverage | 62 (total) |
| 10 | #19 | Advanced Error Handling, Retries & Observability | #20 | 2026-06-10 | Retry policies, X-Ray, CloudWatch alarms/dashboard | 70 (total) |
| 11 | #21 | Full Audio Processing & Output Handling | #22 | 2026-06-11 | Lambda S3 read/write, processing pipeline, output metadata | 73 (total) |
| 12 | #23 | End-to-End Validation, Docs Polish & Completion | #24 | 2026-06-12 | 56 E2E tests, README rewrite, SUMMARY.md | 129 C# + 19 Python = 148 |
| 13 | #25 | Documentation: README Enrichment + Meta-Prompts | #26 | 2026-06-13 | META-PROMPTS.md, enriched README | 148 (unchanged) |
| 14 | #27 | Documentation: Experimental Design (this document) | - | - | EXPERIMENT.md | - |

### Growth Trajectory

```
Tests: 2 -> 12 -> 15 -> 24 -> 31 -> 37 -> 45 -> 62 -> 70 -> 73 -> 148
         (+10) (+3) (+9) (+7) (+6) (+8) (+17) (+8) (+3) (+75)
```

The large jump from 73 to 148 in Issue #12 reflects the addition of 56 end-to-end validation tests and 19 Python Lambda tests, validating the complete pipeline flow.

---

## Prompting Patterns & Meta-Prompts

The development process used and refined six key prompting patterns, documented in full at [docs/META-PROMPTS.md](META-PROMPTS.md):

### Pattern Library Summary

| # | Pattern | Purpose |
|---|---------|---------|
| 1 | **Agent Instruction Template** | Structure guidelines for AI agents working on CDK projects (see [AGENT_GUIDELINES.md](AGENT_GUIDELINES.md)) |
| 2 | **Issue-Driven Development** | Structure issues for agent consumption with clear acceptance criteria, ordered tasks, and success metrics |
| 3 | **TDD Infrastructure Pattern** | Apply Red/Green/Refactor to CDK Assertions with C# examples and assertion patterns |
| 4 | **Architecture-First Development** | Maintain documentation as the authoritative source of truth before any implementation |
| 5 | **Context Propagation** | Use `.agents/tasks/` structure to maintain agent state and findings across sessions |
| 6 | **Error Handling and Observability** | Step Functions retry/catch patterns with CDK code and alarm configuration |

### Issue Structure Template

Every implementation issue followed this structure:

```markdown
## Goal
[One sentence describing the target state]

## Strict TDD Rules
- Write failing tests FIRST
- One resource at a time
- Never skip Red phase

## Specific Requirements
[Numbered list of concrete deliverables]

## Tasks (Strict Order)
1. Write failing test for X
2. Implement X to pass the test
3. Write failing test for Y
...

## Success Criteria
- [ ] All tests pass
- [ ] CDK synth succeeds for all environments
- [ ] No regressions in existing tests

## Next Issue
[Pointer to the next issue in the sequence]
```

This structure eliminates ambiguity and provides clear boundaries for autonomous agent implementation.

---

## Key Decisions & Trade-offs

### Architecture Decisions

| Decision | Rationale | Trade-off |
|----------|-----------|-----------|
| **Step Functions over Lambda chaining** | Built-in retry, visual execution history, SDK integrations reduce code | Higher per-execution cost, state payload size limits |
| **Event-driven architecture** | Loose coupling, independent scaling, fault isolation | Eventual consistency, debugging complexity |
| **DynamoDB on-demand billing** | Bursty workloads, no capacity planning needed | Higher per-request cost at sustained load |
| **Python Lambda with C# CDK** | Fast cold starts, rich AWS SDK, separation of concerns | Two languages in one project, context switching |
| **CustomState for Polly** | No L2 construct available in CDK C# for Polly SDK integration | More verbose, less type safety |
| **Single stack (not nested)** | Simpler deployment, easier testing, fewer cross-stack references | Larger template, longer deploy times |

### Process Decisions

| Decision | Rationale | Trade-off |
|----------|-----------|-----------|
| **Architecture before code** | Prevents drift, provides clear implementation target | Upfront time investment, may need revision |
| **Single feature per issue** | Clear scope, incremental progress, easy rollback | More issues to manage, more PRs to review |
| **Incremental complexity** | Each issue builds on the previous, reducing cognitive load | Later issues depend on earlier ones being correct |
| **148 tests for ~25 resources** | High coverage ratio ensures confidence in changes | Test maintenance overhead, JSII resource pressure |
| **Serial test execution** | JSII runtime constraints prevent parallel execution | Slower test runs (~30s vs potential ~10s) |

---

## Preliminary Observations

### What Worked Well

1. **TDD for infrastructure provides high confidence without deployment.** CDK Assertions validate CloudFormation output at synthesis time, catching IAM, encryption, and configuration issues before any AWS resources are created.

2. **Issue-driven development with explicit acceptance criteria enables autonomous agent implementation.** The structured issue format eliminated back-and-forth and allowed Kiro to implement features independently.

3. **The architecture-first approach reduced implementation ambiguity significantly.** Having ARCHITECTURE.md as a reference eliminated design decisions during implementation, letting the agent focus on correct CDK code.

4. **Context propagation via task files prevents re-discovery of environment quirks across sessions.** The `.agents/tasks/` structure allowed findings (JSII issues, NODE_OPTIONS conflicts) to persist between agent invocations.

5. **CDK Assertions catch IAM, encryption, and configuration issues at synthesis time.** Tests validated least-privilege policies, KMS encryption, and resource wiring without ever deploying to AWS.

6. **Strict TDD discipline was maintained throughout.** No resource was added without a prior failing test, ensuring complete test coverage of all infrastructure.

### Challenges Encountered

1. **JSII resource pressure** was a recurring challenge with large test suites. The .NET/JSII bridge consumes significant memory when synthesizing multiple stacks in parallel, requiring serial test execution.

2. **CustomState verbosity** for AWS SDK integrations (Polly, DynamoDB) required manual JSON construction of task parameters, reducing type safety compared to L2 constructs.

3. **Maintaining architecture documentation alongside code required discipline.** As the implementation evolved, keeping Mermaid diagrams and component descriptions current added overhead.

4. **Step Functions state machine definition complexity grows non-linearly.** As states were added for error handling, retry, and validation, the CDK code became harder to read.

5. **Cross-language testing** (C# infrastructure + Python Lambda) required separate test runners and different assertion patterns.

### Process Insights

- The project went from zero to 148 tests and ~25 AWS resources in **12 working days** (May 28 - June 13, 2026)
- Average velocity: ~12 tests and ~2 resources per implementation issue
- All 12 implementation PRs were merged without requiring rework
- Documentation issues (2) ensured knowledge capture was part of the process, not an afterthought

---

## Final Metrics

| Metric | Value |
|--------|-------|
| **Total automated tests** | 148 (129 C# + 19 Python) |
| **AWS resources (main stack)** | ~25+ |
| **Implementation issues** | 12 |
| **Documentation issues** | 2 |
| **Total PRs merged** | 12 (all by Kiro agent) |
| **Languages** | C# (CDK infrastructure), Python (Lambda) |
| **Security** | KMS encryption on all data stores, least-privilege IAM, public access blocked |
| **Observability** | CloudWatch Logs/Alarms/Dashboard, X-Ray tracing |
| **Development period** | May 28 - June 13, 2026 (16 calendar days) |
| **Test execution mode** | Serial (JSII constraint) |
| **CI platform** | GitHub Actions (.NET 8, Node.js 20) |

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [Architecture](ARCHITECTURE.md) | Detailed system design, data flow, security model, and observability |
| [Agent Guidelines](AGENT_GUIDELINES.md) | Development workflow, TDD practices, and coding conventions |
| [Project Summary](SUMMARY.md) | Key decisions, what was built, and experiment notes |
| [Meta-Prompts](META-PROMPTS.md) | Reusable agentic TDD IaC patterns and prompt templates |
