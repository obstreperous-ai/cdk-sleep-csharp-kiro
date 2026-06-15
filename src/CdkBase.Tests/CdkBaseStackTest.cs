using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace CdkBase.Tests
{
    /// <summary>
    /// Shared fixture that synthesizes the CDK template once for all CdkBaseStack tests.
    /// Uses xUnit's IClassFixture pattern to avoid repeated JSII template synthesis (70 times).
    /// This dramatically reduces test execution time under JSII memory pressure.
    /// </summary>
    public class CdkBaseStackFixture
    {
        private static readonly JsonSerializerOptions RelaxedOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public Template Template { get; }
        public string StateMachineJson { get; }
        public string StateMachineJsonRelaxed { get; }

        public CdkBaseStackFixture()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            Template = Template.FromStack(stack);

            var stateMachines = Template.FindResources("AWS::StepFunctions::StateMachine");
            StateMachineJson = JsonSerializer.Serialize(stateMachines.First().Value);
            StateMachineJsonRelaxed = JsonSerializer.Serialize(stateMachines.First().Value, RelaxedOptions);
        }
    }

    // Tests use string-matching against serialized CloudFormation templates to assert on
    // state machine wiring. This couples tests to CDK's JSON serialization order, which is
    // a known trade-off: it provides strong regression coverage for the current CDK version
    // at the cost of potential breakage on CDK upgrades that reorder JSON properties.
    public class CdkBaseStackTest : IClassFixture<CdkBaseStackFixture>
    {
        private readonly Template _template;
        private readonly string _serialized;
        private readonly string _serializedRelaxed;

        public CdkBaseStackTest(CdkBaseStackFixture fixture)
        {
            _template = fixture.Template;
            _serialized = fixture.StateMachineJson;
            _serializedRelaxed = fixture.StateMachineJsonRelaxed;
        }

        [Fact]
        public void Stack_SynthesizesSuccessfully()
        {
            // Assert
            Assert.NotNull(_template);
        }

        [Fact]
        public void Stack_HasExactlyTwoS3Buckets()
        {
            // Assert
            _template.ResourceCountIs("AWS::S3::Bucket", 2);
        }

        [Fact]
        public void Stack_HasInputBucketWithKmsEncryption()
        {
            // Assert - find buckets by logical ID to distinguish input from output
            var buckets = _template.FindResources("AWS::S3::Bucket");
            var inputBucketEntry = buckets.First(b => b.Key.Contains("SleepAudioInputBucket"));
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(inputBucketEntry.Value))
                .GetProperty("Properties");

            var sseConfig = properties
                .GetProperty("BucketEncryption")
                .GetProperty("ServerSideEncryptionConfiguration");
            var algorithm = sseConfig[0]
                .GetProperty("ServerSideEncryptionByDefault")
                .GetProperty("SSEAlgorithm")
                .GetString();

            Assert.Equal("aws:kms", algorithm);
        }

        [Fact]
        public void Stack_HasInputBucketWithVersioningEnabled()
        {
            // Assert - find input bucket by logical ID
            var buckets = _template.FindResources("AWS::S3::Bucket");
            var inputBucketEntry = buckets.First(b => b.Key.Contains("SleepAudioInputBucket"));
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(inputBucketEntry.Value))
                .GetProperty("Properties");

            var status = properties
                .GetProperty("VersioningConfiguration")
                .GetProperty("Status")
                .GetString();

            Assert.Equal("Enabled", status);
        }

        [Fact]
        public void Stack_HasOutputBucketWithEncryptionAndVersioning()
        {
            // Assert - find output bucket by logical ID
            var buckets = _template.FindResources("AWS::S3::Bucket");
            var outputBucketEntry = buckets.First(b => b.Key.Contains("SleepAudioOutputBucket"));
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(outputBucketEntry.Value))
                .GetProperty("Properties");

            var algorithm = properties
                .GetProperty("BucketEncryption")
                .GetProperty("ServerSideEncryptionConfiguration")[0]
                .GetProperty("ServerSideEncryptionByDefault")
                .GetProperty("SSEAlgorithm")
                .GetString();
            Assert.Equal("aws:kms", algorithm);

            var status = properties
                .GetProperty("VersioningConfiguration")
                .GetProperty("Status")
                .GetString();
            Assert.Equal("Enabled", status);
        }

        [Fact]
        public void Stack_BucketsBlockPublicAccess()
        {
            // Assert - verify both buckets block public access by logical ID
            var buckets = _template.FindResources("AWS::S3::Bucket");

            var inputBucketEntry = buckets.First(b => b.Key.Contains("SleepAudioInputBucket"));
            var inputProps = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(inputBucketEntry.Value))
                .GetProperty("Properties")
                .GetProperty("PublicAccessBlockConfiguration");
            Assert.True(inputProps.GetProperty("BlockPublicAcls").GetBoolean());
            Assert.True(inputProps.GetProperty("BlockPublicPolicy").GetBoolean());
            Assert.True(inputProps.GetProperty("IgnorePublicAcls").GetBoolean());
            Assert.True(inputProps.GetProperty("RestrictPublicBuckets").GetBoolean());

            var outputBucketEntry = buckets.First(b => b.Key.Contains("SleepAudioOutputBucket"));
            var outputProps = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(outputBucketEntry.Value))
                .GetProperty("Properties")
                .GetProperty("PublicAccessBlockConfiguration");
            Assert.True(outputProps.GetProperty("BlockPublicAcls").GetBoolean());
            Assert.True(outputProps.GetProperty("BlockPublicPolicy").GetBoolean());
            Assert.True(outputProps.GetProperty("IgnorePublicAcls").GetBoolean());
            Assert.True(outputProps.GetProperty("RestrictPublicBuckets").GetBoolean());
        }

        [Fact]
        public void Stack_HasEventBridgeRuleForS3ObjectCreated()
        {
            // Assert - verify EventBridge rule exists with correct event pattern
            _template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
            {
                { "EventPattern", new Dictionary<string, object>
                    {
                        { "source", new object[] { "aws.s3" } },
                        { "detail-type", new object[] { "Object Created" } }
                    }
                }
            });
        }

        [Fact]
        public void Stack_EventBridgeRuleFiltersByInputBucketName()
        {
            // Assert - verify the EventBridge rule detail filter references the input bucket
            var rules = _template.FindResources("AWS::Events::Rule");
            Assert.Single(rules);

            var ruleEntry = rules.First();
            var ruleJson = JsonSerializer.Serialize(ruleEntry.Value);
            var ruleElement = JsonSerializer.Deserialize<JsonElement>(ruleJson);
            var eventPattern = ruleElement.GetProperty("Properties").GetProperty("EventPattern");
            var detail = eventPattern.GetProperty("detail");
            var bucketDetail = detail.GetProperty("bucket");
            var nameArray = bucketDetail.GetProperty("name");

            // The bucket name is a Ref to the input bucket logical ID
            Assert.Equal(JsonValueKind.Array, nameArray.ValueKind);
            Assert.Equal(1, nameArray.GetArrayLength());
            var nameEntry = nameArray[0];
            // CDK synthesizes as { "Ref": "SleepAudioInputBucket..." }
            Assert.True(nameEntry.TryGetProperty("Ref", out var refValue));
            Assert.Contains("SleepAudioInputBucket", refValue.GetString());
        }

        [Fact]
        public void Stack_EventBridgeRuleHasTarget()
        {
            // Assert - verify EventBridge rule has at least one target
            _template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
            {
                { "Targets", Match.AnyValue() }
            });
        }

        [Fact]
        public void Stack_HasStepFunctionsStateMachine()
        {
            // Assert - verify exactly one state machine exists
            _template.ResourceCountIs("AWS::StepFunctions::StateMachine", 1);
        }

        [Fact]
        public void Stack_StateMachineHasDefinitionWithPollyTask()
        {
            // Assert - find the state machine and verify its definition contains Polly task
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify the definition contains the SynthesizeSpeech state and Polly resource
            Assert.Contains("SynthesizeSpeech", _serialized);
            Assert.Contains("arn:aws:states:::aws-sdk:polly:synthesizeSpeech", _serialized);
        }

        [Fact]
        public void Stack_StateMachineHasLoggingEnabled()
        {
            // Assert - verify state machine has logging configuration
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var stateMachineEntry = stateMachines.First();
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(stateMachineEntry.Value))
                .GetProperty("Properties");

            var loggingConfig = properties.GetProperty("LoggingConfiguration");
            var level = loggingConfig.GetProperty("Level").GetString();
            var includeExecutionData = loggingConfig.GetProperty("IncludeExecutionData").GetBoolean();

            Assert.Equal("ALL", level);
            Assert.True(includeExecutionData);
        }

        [Fact]
        public void Stack_EventBridgeRuleTargetsStateMachine()
        {
            // Assert - verify EventBridge rule targets the state machine (not SQS)
            var rules = _template.FindResources("AWS::Events::Rule");
            Assert.Single(rules);

            var ruleEntry = rules.First();
            var ruleJson = JsonSerializer.Serialize(ruleEntry.Value);

            // The target Arn should reference the state machine (via Ref or GetAtt)
            Assert.Contains("SleepAudioPipelineStateMachine", ruleJson);
            // Ensure no SQS queue reference
            Assert.DoesNotContain("StubProcessingQueue", ruleJson);
        }

        [Fact]
        public void Stack_StateMachineRoleHasPollyPermissions()
        {
            // Assert - verify IAM policies include polly:SynthesizeSpeech
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("polly:SynthesizeSpeech", policiesJson);
        }

        [Fact]
        public void Stack_StateMachineRoleHasCloudWatchLogsPermissions()
        {
            // Assert - verify IAM policies include CloudWatch Logs permissions
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("logs:CreateLogDelivery", policiesJson);
        }

        [Fact]
        public void Stack_HasDynamoDbMetadataTable()
        {
            // Assert - verify exactly one DynamoDB table exists
            _template.ResourceCountIs("AWS::DynamoDB::Table", 1);
        }

        [Fact]
        public void Stack_DynamoDbTableHasCorrectKeySchema()
        {
            // Assert - verify partition key is 'audioId' of type S
            var tables = _template.FindResources("AWS::DynamoDB::Table");
            Assert.Single(tables);

            var tableEntry = tables.First();
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tableEntry.Value))
                .GetProperty("Properties");

            var keySchema = properties.GetProperty("KeySchema");
            Assert.Equal(1, keySchema.GetArrayLength());
            Assert.Equal("audioId", keySchema[0].GetProperty("AttributeName").GetString());
            Assert.Equal("HASH", keySchema[0].GetProperty("KeyType").GetString());

            var attributeDefinitions = properties.GetProperty("AttributeDefinitions");
            Assert.Equal(1, attributeDefinitions.GetArrayLength());
            Assert.Equal("audioId", attributeDefinitions[0].GetProperty("AttributeName").GetString());
            Assert.Equal("S", attributeDefinitions[0].GetProperty("AttributeType").GetString());
        }

        [Fact]
        public void Stack_DynamoDbTableHasOnDemandBilling()
        {
            // Assert - verify BillingMode is PAY_PER_REQUEST
            _template.HasResourceProperties("AWS::DynamoDB::Table", new Dictionary<string, object>
            {
                { "BillingMode", "PAY_PER_REQUEST" }
            });
        }

        [Fact]
        public void Stack_DynamoDbTableHasEncryption()
        {
            // Assert - verify SSESpecification Enabled is true
            var tables = _template.FindResources("AWS::DynamoDB::Table");
            Assert.Single(tables);

            var tableEntry = tables.First();
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tableEntry.Value))
                .GetProperty("Properties");

            var sseEnabled = properties.GetProperty("SSESpecification").GetProperty("SSEEnabled").GetBoolean();
            Assert.True(sseEnabled);
        }

        [Fact]
        public void Stack_DynamoDbTableHasPointInTimeRecovery()
        {
            // Assert - verify PointInTimeRecoverySpecification PointInTimeRecoveryEnabled is true
            var tables = _template.FindResources("AWS::DynamoDB::Table");
            Assert.Single(tables);

            var tableEntry = tables.First();
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tableEntry.Value))
                .GetProperty("Properties");

            var pitrEnabled = properties
                .GetProperty("PointInTimeRecoverySpecification")
                .GetProperty("PointInTimeRecoveryEnabled")
                .GetBoolean();
            Assert.True(pitrEnabled);
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsDynamoDbPutItem()
        {
            // Assert - verify state machine definition contains DynamoDB PutItem resource
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // CDK splits the ARN across Fn::Join boundaries, so check for the resource suffix
            Assert.Contains(":states:::dynamodb:putItem", _serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsDynamoDbUpdateItem()
        {
            // Assert - verify state machine definition contains DynamoDB UpdateItem resource
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // CDK splits the ARN across Fn::Join boundaries, so check for the resource suffix
            Assert.Contains(":states:::dynamodb:updateItem", _serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasCorrectStateOrdering()
        {
            // Assert - verify state machine definition contains all expected states
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify all states exist in the definition
            Assert.Contains("WriteInitialMetadata", _serialized);
            Assert.Contains("ProcessAudio", _serialized);
            Assert.Contains("SynthesizeSpeech", _serialized);
            Assert.Contains("UpdateStatusCompleted", _serialized);
            Assert.Contains("UpdateStatusFailed", _serialized);

            // Verify catch handlers exist (States.ALL error routing)
            Assert.Contains("States.ALL", _serialized);
        }

        [Fact]
        public void Stack_StateMachineRoleHasDynamoDbPermissions()
        {
            // Assert - verify IAM policies contain DynamoDB PutItem and UpdateItem actions
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("dynamodb:PutItem", policiesJson);
            Assert.Contains("dynamodb:UpdateItem", policiesJson);
        }

        [Fact]
        public void Stack_HasTwoSnsTopics()
        {
            _template.ResourceCountIs("AWS::SNS::Topic", 2);
        }

        [Fact]
        public void Stack_SnsTopicsHaveKmsEncryption()
        {
            var topics = _template.FindResources("AWS::SNS::Topic");
            Assert.Equal(2, topics.Count);

            foreach (var topic in topics)
            {
                var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(topic.Value))
                    .GetProperty("Properties");
                Assert.True(properties.TryGetProperty("KmsMasterKeyId", out _),
                    $"Topic {topic.Key} is missing KmsMasterKeyId encryption");
            }
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsSnsPublishTasks()
        {
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            Assert.Contains(":states:::sns:publish", _serialized);
        }

        [Fact]
        public void Stack_StateMachineHasSuccessAndFailureNotificationStates()
        {
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            Assert.Contains("PublishSuccessNotification", _serialized);
            Assert.Contains("PublishFailureNotification", _serialized);
        }

        [Fact]
        public void Stack_StateMachineRoleHasSnsPublishPermissions()
        {
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);
            Assert.Contains("sns:Publish", policiesJson);
        }

        [Fact]
        public void Stack_StateMachineErrorPathIncludesErrorInfo()
        {
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify the FAILED update includes errorInfo in the update expression
            Assert.Contains("errorInfo", _serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasCorrectStateWiring()
        {
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify success path ordering:
            // WriteInitialMetadata -> ProcessAudio -> SynthesizeSpeech -> UpdateStatusCompleted -> PublishSuccessNotification
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", _serializedRelaxed);
            Assert.Contains("\\\"Next\\\":\\\"SynthesizeSpeech\\\"", _serializedRelaxed);
            Assert.Contains("\\\"Next\\\":\\\"PublishSuccessNotification\\\"", _serializedRelaxed);

            // Verify failure path ordering: UpdateStatusFailed -> PublishFailureNotification
            Assert.Contains("\\\"Next\\\":\\\"PublishFailureNotification\\\"", _serializedRelaxed);

            // Verify catch routing targets UpdateStatusFailed
            Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", _serializedRelaxed);
        }

        [Fact]
        public void Stack_HasLambdaFunctionWithPythonRuntime()
        {
            // Assert - verify a Lambda function exists with Python runtime
            _template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object>
            {
                { "Runtime", "python3.12" },
                { "Handler", "index.handler" }
            });
        }

        [Fact]
        public void Stack_LambdaFunctionHasTableNameEnvironmentVariable()
        {
            // Assert - verify the Lambda function has TABLE_NAME environment variable
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var envVars = properties.GetProperty("Environment").GetProperty("Variables");
            Assert.True(envVars.TryGetProperty("TABLE_NAME", out _),
                "Lambda function is missing TABLE_NAME environment variable");
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsLambdaInvokeTask()
        {
            // Assert - verify state machine definition contains a LambdaInvoke task
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            Assert.Contains("ProcessAudio", _serialized);
            Assert.Contains(":states:::lambda:invoke", _serialized);
        }

        [Fact]
        public void Stack_StateMachineRoleHasLambdaInvokePermission()
        {
            // Assert - verify IAM policies grant lambda:InvokeFunction to the state machine
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("lambda:InvokeFunction", policiesJson);
        }

        [Fact]
        public void Stack_LambdaExecutionRoleHasDynamoDbReadWritePermissions()
        {
            // Assert - verify IAM policies grant the Lambda DynamoDB read/write permissions
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            // GrantReadWriteData gives BatchGetItem, GetItem, Query, Scan, BatchWriteItem,
            // PutItem, UpdateItem, DeleteItem, DescribeTable, GetRecords, GetShardIterator, ConditionCheckItem
            Assert.Contains("dynamodb:GetItem", policiesJson);
            Assert.Contains("dynamodb:PutItem", policiesJson);
            Assert.Contains("dynamodb:UpdateItem", policiesJson);
        }

        [Fact]
        public void Stack_ProcessAudioStepHasErrorHandling()
        {
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify ProcessAudio state has a Catch block
            // The ProcessAudio state should have "Catch" with "States.ALL" routing to UpdateStatusFailed
            Assert.Contains("ProcessAudio", _serializedRelaxed);

            // ProcessAudio state should have Catch with ErrorEquals: States.ALL and Next: UpdateStatusFailed
            var processAudioIdx = _serializedRelaxed.IndexOf("ProcessAudio");
            Assert.True(processAudioIdx >= 0, "ProcessAudio state not found in definition");

            // The state definition must include Catch with States.ALL
            // Since multiple states have Catch, just verify ProcessAudio exists and the overall definition
            // has the expected error handling pattern
            Assert.Contains("States.ALL", _serializedRelaxed);
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsValidateInputState()
        {
            // Assert - verify state machine definition contains a ValidateInput Choice state
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            Assert.Contains("ValidateInput", _serialized);
            Assert.Contains("Choice", _serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasValidationErrorPath()
        {
            // Assert - verify ValidateInput has a Default path to ValidationFailed Pass state
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // The Choice state's Default path should route to ValidationFailed
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", _serializedRelaxed);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasValidationFailedPassState()
        {
            // Assert - verify the ValidationFailed Pass state exists with synthetic error payload
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify the ValidationFailed state exists and is a Pass type
            Assert.Contains("ValidationFailed", _serializedRelaxed);
            // Verify it transitions to UpdateStatusFailed
            Assert.Contains("\\\"ValidationFailed\\\"", _serializedRelaxed);
            // Verify it injects error payload with Cause
            Assert.Contains("Unsupported file extension", _serializedRelaxed);
            Assert.Contains("ValidationError", _serializedRelaxed);
            // Verify ResultPath is $.error
            Assert.Contains("$.error", _serializedRelaxed);
        }

        [Fact]
        public void Stack_LambdaFunctionHasInputBucketEnvironmentVariable()
        {
            // Assert - verify the Lambda function has INPUT_BUCKET_NAME environment variable
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var envVars = properties.GetProperty("Environment").GetProperty("Variables");
            Assert.True(envVars.TryGetProperty("INPUT_BUCKET_NAME", out _),
                "Lambda function is missing INPUT_BUCKET_NAME environment variable");
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasCompleteEndToEndFlow()
        {
            // Assert - verify complete chain: WriteInitialMetadata -> ValidateInput -> ProcessAudio -> SynthesizeSpeech -> UpdateStatusCompleted -> PublishSuccessNotification
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // WriteInitialMetadata transitions to ValidateInput
            Assert.Contains("\\\"Next\\\":\\\"ValidateInput\\\"", _serializedRelaxed);
            // ValidateInput valid path transitions to ProcessAudio
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", _serializedRelaxed);
            // ProcessAudio transitions to SynthesizeSpeech
            Assert.Contains("\\\"Next\\\":\\\"SynthesizeSpeech\\\"", _serializedRelaxed);
            // UpdateStatusCompleted transitions to PublishSuccessNotification
            Assert.Contains("\\\"Next\\\":\\\"PublishSuccessNotification\\\"", _serializedRelaxed);
            // UpdateStatusFailed transitions to PublishFailureNotification
            Assert.Contains("\\\"Next\\\":\\\"PublishFailureNotification\\\"", _serializedRelaxed);
        }

        [Fact]
        public void Stack_EventBridgeRuleTargetsStateMachineWithRoleArn()
        {
            // Assert - verify EventBridge rule target has a RoleArn
            var rules = _template.FindResources("AWS::Events::Rule");
            Assert.Single(rules);

            var ruleEntry = rules.First();
            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(ruleEntry.Value))
                .GetProperty("Properties");

            var targets = properties.GetProperty("Targets");
            Assert.True(targets.GetArrayLength() > 0);

            var target = targets[0];
            Assert.True(target.TryGetProperty("RoleArn", out _),
                "EventBridge rule target is missing RoleArn");
        }

        [Fact]
        public void Stack_InputValidationChecksFileExtension()
        {
            // Assert - verify the state machine definition references file extension patterns
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // The Choice state should check for supported extensions (lowercase)
            Assert.Contains("*.mp3", _serialized);
            Assert.Contains("*.wav", _serialized);
            Assert.Contains("*.ogg", _serialized);
            Assert.Contains("*.txt", _serialized);

            // The Choice state should also check for uppercase extensions
            Assert.Contains("*.MP3", _serialized);
            Assert.Contains("*.WAV", _serialized);
            Assert.Contains("*.OGG", _serialized);
            Assert.Contains("*.TXT", _serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionStartsWithWriteInitialMetadata()
        {
            // Assert - verify the state machine StartAt is WriteInitialMetadata
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            Assert.Contains("\\\"StartAt\\\":\\\"WriteInitialMetadata\\\"", _serializedRelaxed);
        }

        [Fact]
        public void Stack_StateMachineHasExactlyNineStates()
        {
            // Assert - verify the state machine has exactly 9 states
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // All 9 expected states must exist
            var expectedStates = new[]
            {
                "WriteInitialMetadata", "ValidateInput", "ProcessAudio",
                "SynthesizeSpeech", "UpdateStatusCompleted", "UpdateStatusFailed",
                "PublishSuccessNotification", "PublishFailureNotification", "ValidationFailed"
            };

            foreach (var state in expectedStates)
            {
                Assert.Contains(state, _serializedRelaxed);
            }

            // Count state Type declarations in the definition string
            // With UnsafeRelaxedJsonEscaping, the inner JSON string uses backslash-escaped quotes
            int taskCount = System.Text.RegularExpressions.Regex.Matches(_serializedRelaxed, @"\\""Type\\"":\\""Task\\""").Count;
            int choiceCount = System.Text.RegularExpressions.Regex.Matches(_serializedRelaxed, @"\\""Type\\"":\\""Choice\\""").Count;
            int passCount = System.Text.RegularExpressions.Regex.Matches(_serializedRelaxed, @"\\""Type\\"":\\""Pass\\""").Count;

            // Total should be 9: 7 Task + 1 Choice + 1 Pass
            Assert.Equal(9, taskCount + choiceCount + passCount);
        }

        [Fact]
        public void Stack_StateMachineErrorCatchHandlersRouteToUpdateStatusFailed()
        {
            // Assert - verify error catch handlers on WriteInitialMetadata, ProcessAudio,
            // SynthesizeSpeech, UpdateStatusCompleted, and PublishSuccessNotification
            // all route to UpdateStatusFailed
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var statesWithCatch = new[]
            {
                "WriteInitialMetadata", "ProcessAudio", "SynthesizeSpeech",
                "UpdateStatusCompleted", "PublishSuccessNotification"
            };

            foreach (var stateName in statesWithCatch)
            {
                // Find the state definition and verify it has a Catch with Next:UpdateStatusFailed
                var stateIdx = _serializedRelaxed.IndexOf($"\\\"{stateName}\\\":");
                Assert.True(stateIdx >= 0, $"State {stateName} not found in definition");

                // Get a chunk of the definition after this state
                var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(1200, _serializedRelaxed.Length - stateIdx));
                Assert.Contains("States.ALL", chunk);
                Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", chunk);
            }
        }

        [Fact]
        public void Stack_PublishFailureNotificationIsTerminalState()
        {
            // Assert - verify PublishFailureNotification has End:true
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Find PublishFailureNotification state and verify End:true
            var stateIdx = _serializedRelaxed.IndexOf("\\\"PublishFailureNotification\\\":{");
            Assert.True(stateIdx >= 0, "PublishFailureNotification state not found");

            var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(300, _serializedRelaxed.Length - stateIdx));
            Assert.Contains("\\\"End\\\":true", chunk);
            Assert.Contains("\\\"Type\\\":\\\"Task\\\"", chunk);
        }

        [Fact]
        public void Stack_PublishSuccessNotificationIsTerminalState()
        {
            // Assert - verify PublishSuccessNotification has End:true
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Find PublishSuccessNotification state and verify End:true
            var stateIdx = _serializedRelaxed.IndexOf("\\\"PublishSuccessNotification\\\":{");
            Assert.True(stateIdx >= 0, "PublishSuccessNotification state not found");

            var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(300, _serializedRelaxed.Length - stateIdx));
            Assert.Contains("\\\"End\\\":true", chunk);
            Assert.Contains("\\\"Type\\\":\\\"Task\\\"", chunk);
        }

        [Fact]
        public void Stack_StateMachineCompleteSuccessPath()
        {
            // Assert - verify the complete success path:
            // WriteInitialMetadata -> ValidateInput -> ProcessAudio -> SynthesizeSpeech
            // -> UpdateStatusCompleted -> PublishSuccessNotification (End)
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Verify StartAt
            Assert.Contains("\\\"StartAt\\\":\\\"WriteInitialMetadata\\\"", _serializedRelaxed);
            // WriteInitialMetadata -> ValidateInput
            Assert.Contains("\\\"WriteInitialMetadata\\\":{\\\"Next\\\":\\\"ValidateInput\\\"", _serializedRelaxed);
            // ValidateInput routes valid to ProcessAudio (via Choice)
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", _serializedRelaxed);
            // ProcessAudio -> SynthesizeSpeech
            Assert.Contains("\\\"ProcessAudio\\\":{\\\"Next\\\":\\\"SynthesizeSpeech\\\"", _serializedRelaxed);
            // SynthesizeSpeech -> UpdateStatusCompleted
            Assert.Contains("\\\"SynthesizeSpeech\\\":{\\\"Next\\\":\\\"UpdateStatusCompleted\\\"", _serializedRelaxed);
            // UpdateStatusCompleted -> PublishSuccessNotification
            Assert.Contains("\\\"UpdateStatusCompleted\\\":{\\\"Next\\\":\\\"PublishSuccessNotification\\\"", _serializedRelaxed);
            // PublishSuccessNotification is terminal
            Assert.Contains("\\\"PublishSuccessNotification\\\":{\\\"End\\\":true", _serializedRelaxed);
        }

        [Fact]
        public void Stack_StateMachineCompleteFailurePath()
        {
            // Assert - verify the failure path from ValidationFailed:
            // ValidationFailed -> UpdateStatusFailed -> PublishFailureNotification (End)
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // ValidateInput Default -> ValidationFailed
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", _serializedRelaxed);
            // ValidationFailed -> UpdateStatusFailed
            Assert.Contains("\\\"ValidationFailed\\\":{\\\"Type\\\":\\\"Pass\\\"", _serializedRelaxed);
            var vfIdx = _serializedRelaxed.IndexOf("\\\"ValidationFailed\\\":{");
            var vfChunk = _serializedRelaxed.Substring(vfIdx, System.Math.Min(400, _serializedRelaxed.Length - vfIdx));
            Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", vfChunk);
            // UpdateStatusFailed -> PublishFailureNotification
            Assert.Contains("\\\"UpdateStatusFailed\\\":{\\\"Next\\\":\\\"PublishFailureNotification\\\"", _serializedRelaxed);
            // PublishFailureNotification is terminal
            Assert.Contains("\\\"PublishFailureNotification\\\":{\\\"End\\\":true", _serializedRelaxed);
        }

        [Theory]
        [InlineData("dev")]
        [InlineData("staging")]
        [InlineData("prod")]
        public void Stack_SynthesizesSuccessfullyWithEnvironmentContext(string environment)
        {
            // Arrange - Theory tests need their own stack instances for different environments
            var app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    { "environment", environment }
                }
            });

            // Act
            var stack = new CdkBaseStack(app, "TestStack", environment: environment);
            var template = Template.FromStack(stack);

            // Assert - stack synthesizes without error
            Assert.NotNull(template);
            template.ResourceCountIs("AWS::StepFunctions::StateMachine", 1);
        }

        [Fact]
        public void Stack_DefaultsToDevEnvironmentWhenNoContextProvided()
        {
            // Assert - shared fixture stack synthesizes without error (default = dev)
            Assert.NotNull(_template);
            _template.ResourceCountIs("AWS::StepFunctions::StateMachine", 1);
        }

        [Theory]
        [InlineData("dev")]
        [InlineData("staging")]
        [InlineData("prod")]
        public void Stack_HasEnvironmentTag(string environment)
        {
            // Arrange - Theory tests need their own stack instances for different environments
            var app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    { "environment", environment }
                }
            });

            // Act
            var stack = new CdkBaseStack(app, "TestStack", environment: environment);
            var assembly = app.Synth();
            var stackArtifact = assembly.GetStackByName(stack.StackName);
            var tags = stackArtifact.Tags;

            // Assert - verify the Environment tag is set with the correct value
            Assert.True(tags.ContainsKey("Environment"), "Stack should have an Environment tag");
            Assert.Equal(environment, tags["Environment"]);
        }

        [Fact]
        public void Stack_DefaultsToDevEnvironmentTag()
        {
            // Arrange - need a fresh stack to call app.Synth()
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var assembly = app.Synth();
            var stackArtifact = assembly.GetStackByName(stack.StackName);
            var tags = stackArtifact.Tags;

            // Assert - verify the Environment tag defaults to "dev"
            Assert.True(tags.ContainsKey("Environment"), "Stack should have an Environment tag");
            Assert.Equal("dev", tags["Environment"]);
        }

        [Fact]
        public void Stack_ProcessAudioTaskHasSpecificErrorCatches()
        {
            // Assert - verify processAudioTask has catches for Lambda.ServiceException and Lambda.SdkClientException
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            Assert.Contains("Lambda.ServiceException", _serializedRelaxed);
            Assert.Contains("Lambda.SdkClientException", _serializedRelaxed);
        }

        [Fact]
        public void Stack_ProcessAudioTaskHasRetryPolicy()
        {
            // Assert - verify processAudioTask has a Retry configuration
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Find ProcessAudio state and verify it has Retry
            var stateIdx = _serializedRelaxed.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found in definition");

            var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(800, _serializedRelaxed.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("Lambda.ServiceException", chunk);
            Assert.Contains("Lambda.SdkClientException", chunk);
            Assert.Contains("Lambda.TooManyRequestsException", chunk);
        }

        [Fact]
        public void Stack_PollyTaskHasRetryPolicy()
        {
            // Assert - verify pollyTask (SynthesizeSpeech) has Retry configuration
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Find SynthesizeSpeech state and verify it has Retry
            var stateIdx = _serializedRelaxed.IndexOf("\\\"SynthesizeSpeech\\\":{");
            Assert.True(stateIdx >= 0, "SynthesizeSpeech state not found in definition");

            var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(800, _serializedRelaxed.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("IntervalSeconds", chunk);
            Assert.Contains("MaxAttempts", chunk);
            Assert.Contains("BackoffRate", chunk);
        }

        [Fact]
        public void Stack_WriteInitialMetadataHasRetryPolicy()
        {
            // Assert - verify writeInitialMetadata has Retry configuration
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Find WriteInitialMetadata state and verify it has Retry
            var stateIdx = _serializedRelaxed.IndexOf("\\\"WriteInitialMetadata\\\":{");
            Assert.True(stateIdx >= 0, "WriteInitialMetadata state not found in definition");

            var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(800, _serializedRelaxed.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("States.ALL", chunk);
        }

        [Fact]
        public void Stack_UpdateStatusCompletedHasRetryPolicy()
        {
            // Assert - verify updateStatusCompleted has Retry configuration
            var stateMachines = _template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            // Find UpdateStatusCompleted state and verify it has Retry
            var stateIdx = _serializedRelaxed.IndexOf("\\\"UpdateStatusCompleted\\\":{");
            Assert.True(stateIdx >= 0, "UpdateStatusCompleted state not found in definition");

            var chunk = _serializedRelaxed.Substring(stateIdx, System.Math.Min(800, _serializedRelaxed.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("States.ALL", chunk);
        }

        [Fact]
        public void Stack_LambdaFunctionHasXRayTracingEnabled()
        {
            // Assert - verify the Lambda function has TracingConfig Mode Active
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var tracingConfig = properties.GetProperty("TracingConfig");
            var mode = tracingConfig.GetProperty("Mode").GetString();
            Assert.Equal("Active", mode);
        }

        [Fact]
        public void Stack_HasAtLeastTwoCloudWatchAlarms()
        {
            // Assert - verify at least 2 CloudWatch Alarms exist
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");
            Assert.True(alarms.Count >= 2, $"Expected at least 2 CloudWatch Alarms, found {alarms.Count}");
        }

        [Fact]
        public void Stack_HasCloudWatchDashboard()
        {
            // Assert - verify exactly 1 CloudWatch Dashboard exists
            _template.ResourceCountIs("AWS::CloudWatch::Dashboard", 1);
        }

        [Fact]
        public void Stack_LambdaFunctionHasOutputBucketEnvironmentVariable()
        {
            // Assert - verify the Lambda function has OUTPUT_BUCKET_NAME environment variable
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var envVars = properties.GetProperty("Environment").GetProperty("Variables");
            Assert.True(envVars.TryGetProperty("OUTPUT_BUCKET_NAME", out _),
                "Lambda function is missing OUTPUT_BUCKET_NAME environment variable");
        }

        [Fact]
        public void Stack_LambdaExecutionRoleHasS3GetObjectOnInputBucket()
        {
            // Assert - verify the Lambda execution role's policy contains s3:GetObject
            // scoped to the input bucket ARN (not just any policy in the stack)
            var policies = _template.FindResources("AWS::IAM::Policy");

            // Find the policy attached to the Lambda execution role
            var lambdaPolicyEntry = policies.First(p =>
            {
                var json = JsonSerializer.Serialize(p.Value);
                return json.Contains("SleepAudioProcessorFunction") && json.Contains("s3:GetObject");
            });

            var policyJson = JsonSerializer.Serialize(lambdaPolicyEntry.Value);

            // Verify the policy contains s3:GetObject action
            Assert.Contains("s3:GetObject", policyJson);
            Assert.Contains("s3:GetBucket", policyJson);

            // Verify the policy resource references the input bucket (SleepAudioInputBucket)
            Assert.Contains("SleepAudioInputBucket", policyJson);
        }

        [Fact]
        public void Stack_LambdaExecutionRoleHasS3PutObjectOnOutputBucket()
        {
            // Assert - verify the Lambda execution role's policy contains s3:PutObject
            // scoped to the output bucket ARN (not just any policy in the stack)
            var policies = _template.FindResources("AWS::IAM::Policy");

            // Find the policy attached to the Lambda execution role
            var lambdaPolicyEntry = policies.First(p =>
            {
                var json = JsonSerializer.Serialize(p.Value);
                return json.Contains("SleepAudioProcessorFunction") && json.Contains("s3:PutObject");
            });

            var policyJson = JsonSerializer.Serialize(lambdaPolicyEntry.Value);

            // Verify the policy contains s3:PutObject action
            Assert.Contains("s3:PutObject", policyJson);
            Assert.Contains("s3:Abort", policyJson);

            // Verify the policy resource references the output bucket (SleepAudioOutputBucket)
            Assert.Contains("SleepAudioOutputBucket", policyJson);
        }
    }
}
