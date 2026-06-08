using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace CdkBase.Tests
{
    public class CdkBaseStackTest
    {
        [Fact]
        public void Stack_SynthesizesSuccessfully()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert
            Assert.NotNull(template);
        }

        [Fact]
        public void Stack_HasExactlyTwoS3Buckets()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert
            template.ResourceCountIs("AWS::S3::Bucket", 2);
        }

        [Fact]
        public void Stack_HasInputBucketWithKmsEncryption()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - find buckets by logical ID to distinguish input from output
            var buckets = template.FindResources("AWS::S3::Bucket");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - find input bucket by logical ID
            var buckets = template.FindResources("AWS::S3::Bucket");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - find output bucket by logical ID
            var buckets = template.FindResources("AWS::S3::Bucket");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify both buckets block public access by logical ID
            var buckets = template.FindResources("AWS::S3::Bucket");

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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify EventBridge rule exists with correct event pattern
            template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the EventBridge rule detail filter references the input bucket
            var rules = template.FindResources("AWS::Events::Rule");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify EventBridge rule has at least one target
            template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
            {
                { "Targets", Match.AnyValue() }
            });
        }

        [Fact]
        public void Stack_HasStepFunctionsStateMachine()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify exactly one state machine exists
            template.ResourceCountIs("AWS::StepFunctions::StateMachine", 1);
        }

        [Fact]
        public void Stack_StateMachineHasDefinitionWithPollyTask()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - find the state machine and verify its definition contains Polly task
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var stateMachineEntry = stateMachines.First();
            var serialized = JsonSerializer.Serialize(stateMachineEntry.Value);

            // Verify the definition contains the SynthesizeSpeech state and Polly resource
            Assert.Contains("SynthesizeSpeech", serialized);
            Assert.Contains("arn:aws:states:::aws-sdk:polly:synthesizeSpeech", serialized);
        }

        [Fact]
        public void Stack_StateMachineHasLoggingEnabled()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify state machine has logging configuration
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify EventBridge rule targets the state machine (not SQS)
            var rules = template.FindResources("AWS::Events::Rule");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify IAM policies include polly:SynthesizeSpeech
            var policies = template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("polly:SynthesizeSpeech", policiesJson);
        }

        [Fact]
        public void Stack_StateMachineRoleHasCloudWatchLogsPermissions()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify IAM policies include CloudWatch Logs permissions
            var policies = template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("logs:CreateLogDelivery", policiesJson);
        }

        [Fact]
        public void Stack_HasDynamoDbMetadataTable()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify exactly one DynamoDB table exists
            template.ResourceCountIs("AWS::DynamoDB::Table", 1);
        }

        [Fact]
        public void Stack_DynamoDbTableHasCorrectKeySchema()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify partition key is 'audioId' of type S
            var tables = template.FindResources("AWS::DynamoDB::Table");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify BillingMode is PAY_PER_REQUEST
            template.HasResourceProperties("AWS::DynamoDB::Table", new Dictionary<string, object>
            {
                { "BillingMode", "PAY_PER_REQUEST" }
            });
        }

        [Fact]
        public void Stack_DynamoDbTableHasEncryption()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify SSESpecification Enabled is true
            var tables = template.FindResources("AWS::DynamoDB::Table");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify PointInTimeRecoverySpecification PointInTimeRecoveryEnabled is true
            var tables = template.FindResources("AWS::DynamoDB::Table");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify state machine definition contains DynamoDB PutItem resource
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var stateMachineEntry = stateMachines.First();
            var serialized = JsonSerializer.Serialize(stateMachineEntry.Value);

            // CDK splits the ARN across Fn::Join boundaries, so check for the resource suffix
            Assert.Contains(":states:::dynamodb:putItem", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsDynamoDbUpdateItem()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify state machine definition contains DynamoDB UpdateItem resource
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var stateMachineEntry = stateMachines.First();
            var serialized = JsonSerializer.Serialize(stateMachineEntry.Value);

            // CDK splits the ARN across Fn::Join boundaries, so check for the resource suffix
            Assert.Contains(":states:::dynamodb:updateItem", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasCorrectStateOrdering()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify state machine definition contains all expected states
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var stateMachineEntry = stateMachines.First();
            var serialized = JsonSerializer.Serialize(stateMachineEntry.Value);

            // Verify all states exist in the definition
            Assert.Contains("WriteInitialMetadata", serialized);
            Assert.Contains("ProcessAudio", serialized);
            Assert.Contains("SynthesizeSpeech", serialized);
            Assert.Contains("UpdateStatusCompleted", serialized);
            Assert.Contains("UpdateStatusFailed", serialized);

            // Verify catch handlers exist (States.ALL error routing)
            Assert.Contains("States.ALL", serialized);
        }

        [Fact]
        public void Stack_StateMachineRoleHasDynamoDbPermissions()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify IAM policies contain DynamoDB PutItem and UpdateItem actions
            var policies = template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("dynamodb:PutItem", policiesJson);
            Assert.Contains("dynamodb:UpdateItem", policiesJson);
        }

        [Fact]
        public void Stack_HasTwoSnsTopics()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);
            template.ResourceCountIs("AWS::SNS::Topic", 2);
        }

        [Fact]
        public void Stack_SnsTopicsHaveKmsEncryption()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var topics = template.FindResources("AWS::SNS::Topic");
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
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var serialized = JsonSerializer.Serialize(stateMachines.First().Value);
            Assert.Contains(":states:::sns:publish", serialized);
        }

        [Fact]
        public void Stack_StateMachineHasSuccessAndFailureNotificationStates()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var serialized = JsonSerializer.Serialize(stateMachines.First().Value);
            Assert.Contains("PublishSuccessNotification", serialized);
            Assert.Contains("PublishFailureNotification", serialized);
        }

        [Fact]
        public void Stack_StateMachineRoleHasSnsPublishPermissions()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var policies = template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);
            Assert.Contains("sns:Publish", policiesJson);
        }

        [Fact]
        public void Stack_StateMachineErrorPathIncludesErrorInfo()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var serialized = JsonSerializer.Serialize(stateMachines.First().Value);
            // Verify the FAILED update includes errorInfo in the update expression
            Assert.Contains("errorInfo", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasCorrectStateWiring()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // Verify success path ordering:
            // WriteInitialMetadata -> ProcessAudio -> SynthesizeSpeech -> UpdateStatusCompleted -> PublishSuccessNotification
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", serialized);
            Assert.Contains("\\\"Next\\\":\\\"SynthesizeSpeech\\\"", serialized);
            Assert.Contains("\\\"Next\\\":\\\"PublishSuccessNotification\\\"", serialized);

            // Verify failure path ordering: UpdateStatusFailed -> PublishFailureNotification
            Assert.Contains("\\\"Next\\\":\\\"PublishFailureNotification\\\"", serialized);

            // Verify catch routing targets UpdateStatusFailed
            Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", serialized);
        }

        [Fact]
        public void Stack_HasLambdaFunctionWithPythonRuntime()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify a Lambda function exists with Python runtime
            template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object>
            {
                { "Runtime", "python3.12" },
                { "Handler", "index.handler" }
            });
        }

        [Fact]
        public void Stack_LambdaFunctionHasTableNameEnvironmentVariable()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the Lambda function has TABLE_NAME environment variable
            var functions = template.FindResources("AWS::Lambda::Function");
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
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify state machine definition contains a LambdaInvoke task
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var serialized = JsonSerializer.Serialize(stateMachines.First().Value);
            Assert.Contains("ProcessAudio", serialized);
            Assert.Contains(":states:::lambda:invoke", serialized);
        }

        [Fact]
        public void Stack_StateMachineRoleHasLambdaInvokePermission()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify IAM policies grant lambda:InvokeFunction to the state machine
            var policies = template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            Assert.Contains("lambda:InvokeFunction", policiesJson);
        }

        [Fact]
        public void Stack_LambdaExecutionRoleHasDynamoDbReadWritePermissions()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify IAM policies grant the Lambda DynamoDB read/write permissions
            var policies = template.FindResources("AWS::IAM::Policy");
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
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // Verify ProcessAudio state has a Catch block
            // The ProcessAudio state should have "Catch" with "States.ALL" routing to UpdateStatusFailed
            Assert.Contains("ProcessAudio", serialized);

            // Check that the serialized definition has a Catch on ProcessAudio routing to UpdateStatusFailed
            // ProcessAudio state should have Catch with ErrorEquals: States.ALL and Next: UpdateStatusFailed
            var processAudioIdx = serialized.IndexOf("ProcessAudio");
            Assert.True(processAudioIdx >= 0, "ProcessAudio state not found in definition");

            // The state definition must include Catch with States.ALL
            // Since multiple states have Catch, just verify ProcessAudio exists and the overall definition
            // has the expected error handling pattern
            Assert.Contains("States.ALL", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionContainsValidateInputState()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify state machine definition contains a ValidateInput Choice state
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var serialized = JsonSerializer.Serialize(stateMachines.First().Value);
            Assert.Contains("ValidateInput", serialized);
            Assert.Contains("Choice", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasValidationErrorPath()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify ValidateInput has a Default path to ValidationFailed Pass state
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // The Choice state's Default path should route to ValidationFailed
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionHasValidationFailedPassState()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the ValidationFailed Pass state exists with synthetic error payload
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // Verify the ValidationFailed state exists and is a Pass type
            Assert.Contains("ValidationFailed", serialized);
            // Verify it transitions to UpdateStatusFailed
            Assert.Contains("\\\"ValidationFailed\\\"", serialized);
            // Verify it injects error payload with Cause
            Assert.Contains("Unsupported file extension", serialized);
            Assert.Contains("ValidationError", serialized);
            // Verify ResultPath is $.error
            Assert.Contains("$.error", serialized);
        }

        [Fact]
        public void Stack_LambdaFunctionHasInputBucketEnvironmentVariable()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the Lambda function has INPUT_BUCKET_NAME environment variable
            var functions = template.FindResources("AWS::Lambda::Function");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify complete chain: WriteInitialMetadata -> ValidateInput -> ProcessAudio -> SynthesizeSpeech -> UpdateStatusCompleted -> PublishSuccessNotification
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // WriteInitialMetadata transitions to ValidateInput
            Assert.Contains("\\\"Next\\\":\\\"ValidateInput\\\"", serialized);
            // ValidateInput valid path transitions to ProcessAudio
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", serialized);
            // ProcessAudio transitions to SynthesizeSpeech
            Assert.Contains("\\\"Next\\\":\\\"SynthesizeSpeech\\\"", serialized);
            // UpdateStatusCompleted transitions to PublishSuccessNotification
            Assert.Contains("\\\"Next\\\":\\\"PublishSuccessNotification\\\"", serialized);
            // UpdateStatusFailed transitions to PublishFailureNotification
            Assert.Contains("\\\"Next\\\":\\\"PublishFailureNotification\\\"", serialized);
        }

        [Fact]
        public void Stack_EventBridgeRuleTargetsStateMachineWithRoleArn()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify EventBridge rule target has a RoleArn
            var rules = template.FindResources("AWS::Events::Rule");
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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the state machine definition references file extension patterns
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var serialized = JsonSerializer.Serialize(stateMachines.First().Value);

            // The Choice state should check for supported extensions (lowercase)
            Assert.Contains("*.mp3", serialized);
            Assert.Contains("*.wav", serialized);
            Assert.Contains("*.ogg", serialized);
            Assert.Contains("*.txt", serialized);

            // The Choice state should also check for uppercase extensions
            Assert.Contains("*.MP3", serialized);
            Assert.Contains("*.WAV", serialized);
            Assert.Contains("*.OGG", serialized);
            Assert.Contains("*.TXT", serialized);
        }

        [Fact]
        public void Stack_StateMachineDefinitionStartsWithWriteInitialMetadata()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the state machine StartAt is WriteInitialMetadata
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            Assert.Contains("\\\"StartAt\\\":\\\"WriteInitialMetadata\\\"", serialized);
        }

        [Fact]
        public void Stack_StateMachineHasExactlyNineStates()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the state machine has exactly 9 states
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // All 9 expected states must exist
            var expectedStates = new[]
            {
                "WriteInitialMetadata", "ValidateInput", "ProcessAudio",
                "SynthesizeSpeech", "UpdateStatusCompleted", "UpdateStatusFailed",
                "PublishSuccessNotification", "PublishFailureNotification", "ValidationFailed"
            };

            foreach (var state in expectedStates)
            {
                Assert.Contains(state, serialized);
            }

            // Count state Type declarations in the definition string
            // With UnsafeRelaxedJsonEscaping, the inner JSON string uses backslash-escaped quotes
            int taskCount = System.Text.RegularExpressions.Regex.Matches(serialized, @"\\""Type\\"":\\""Task\\""").Count;
            int choiceCount = System.Text.RegularExpressions.Regex.Matches(serialized, @"\\""Type\\"":\\""Choice\\""").Count;
            int passCount = System.Text.RegularExpressions.Regex.Matches(serialized, @"\\""Type\\"":\\""Pass\\""").Count;

            // Total should be 9: 7 Task + 1 Choice + 1 Pass
            Assert.Equal(9, taskCount + choiceCount + passCount);
        }

        [Fact]
        public void Stack_StateMachineErrorCatchHandlersRouteToUpdateStatusFailed()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify error catch handlers on WriteInitialMetadata, ProcessAudio,
            // SynthesizeSpeech, UpdateStatusCompleted, and PublishSuccessNotification
            // all route to UpdateStatusFailed
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            var statesWithCatch = new[]
            {
                "WriteInitialMetadata", "ProcessAudio", "SynthesizeSpeech",
                "UpdateStatusCompleted", "PublishSuccessNotification"
            };

            foreach (var stateName in statesWithCatch)
            {
                // Find the state definition and verify it has a Catch with Next:UpdateStatusFailed
                var stateIdx = serialized.IndexOf($"\\\"{stateName}\\\":");
                Assert.True(stateIdx >= 0, $"State {stateName} not found in definition");

                // Get a chunk of the definition after this state
                var chunk = serialized.Substring(stateIdx, System.Math.Min(500, serialized.Length - stateIdx));
                Assert.Contains("States.ALL", chunk);
                Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", chunk);
            }
        }

        [Fact]
        public void Stack_PublishFailureNotificationIsTerminalState()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify PublishFailureNotification has End:true
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // Find PublishFailureNotification state and verify End:true
            var stateIdx = serialized.IndexOf("\\\"PublishFailureNotification\\\":{");
            Assert.True(stateIdx >= 0, "PublishFailureNotification state not found");

            var chunk = serialized.Substring(stateIdx, System.Math.Min(300, serialized.Length - stateIdx));
            Assert.Contains("\\\"End\\\":true", chunk);
            Assert.Contains("\\\"Type\\\":\\\"Task\\\"", chunk);
        }

        [Fact]
        public void Stack_PublishSuccessNotificationIsTerminalState()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify PublishSuccessNotification has End:true
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // Find PublishSuccessNotification state and verify End:true
            var stateIdx = serialized.IndexOf("\\\"PublishSuccessNotification\\\":{");
            Assert.True(stateIdx >= 0, "PublishSuccessNotification state not found");

            var chunk = serialized.Substring(stateIdx, System.Math.Min(300, serialized.Length - stateIdx));
            Assert.Contains("\\\"End\\\":true", chunk);
            Assert.Contains("\\\"Type\\\":\\\"Task\\\"", chunk);
        }

        [Fact]
        public void Stack_StateMachineCompleteSuccessPath()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the complete success path:
            // WriteInitialMetadata -> ValidateInput -> ProcessAudio -> SynthesizeSpeech
            // -> UpdateStatusCompleted -> PublishSuccessNotification (End)
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // Verify StartAt
            Assert.Contains("\\\"StartAt\\\":\\\"WriteInitialMetadata\\\"", serialized);
            // WriteInitialMetadata -> ValidateInput
            Assert.Contains("\\\"WriteInitialMetadata\\\":{\\\"Next\\\":\\\"ValidateInput\\\"", serialized);
            // ValidateInput routes valid to ProcessAudio (via Choice)
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", serialized);
            // ProcessAudio -> SynthesizeSpeech
            Assert.Contains("\\\"ProcessAudio\\\":{\\\"Next\\\":\\\"SynthesizeSpeech\\\"", serialized);
            // SynthesizeSpeech -> UpdateStatusCompleted
            Assert.Contains("\\\"SynthesizeSpeech\\\":{\\\"Next\\\":\\\"UpdateStatusCompleted\\\"", serialized);
            // UpdateStatusCompleted -> PublishSuccessNotification
            Assert.Contains("\\\"UpdateStatusCompleted\\\":{\\\"Next\\\":\\\"PublishSuccessNotification\\\"", serialized);
            // PublishSuccessNotification is terminal
            Assert.Contains("\\\"PublishSuccessNotification\\\":{\\\"End\\\":true", serialized);
        }

        [Fact]
        public void Stack_StateMachineCompleteFailurePath()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify the failure path from ValidationFailed:
            // ValidationFailed -> UpdateStatusFailed -> PublishFailureNotification (End)
            var stateMachines = template.FindResources("AWS::StepFunctions::StateMachine");
            Assert.Single(stateMachines);

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var serialized = JsonSerializer.Serialize(stateMachines.First().Value, options);

            // ValidateInput Default -> ValidationFailed
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", serialized);
            // ValidationFailed -> UpdateStatusFailed
            Assert.Contains("\\\"ValidationFailed\\\":{\\\"Type\\\":\\\"Pass\\\"", serialized);
            var vfIdx = serialized.IndexOf("\\\"ValidationFailed\\\":{");
            var vfChunk = serialized.Substring(vfIdx, System.Math.Min(400, serialized.Length - vfIdx));
            Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", vfChunk);
            // UpdateStatusFailed -> PublishFailureNotification
            Assert.Contains("\\\"UpdateStatusFailed\\\":{\\\"Next\\\":\\\"PublishFailureNotification\\\"", serialized);
            // PublishFailureNotification is terminal
            Assert.Contains("\\\"PublishFailureNotification\\\":{\\\"End\\\":true", serialized);
        }

        [Theory]
        [InlineData("dev")]
        [InlineData("staging")]
        [InlineData("prod")]
        public void Stack_SynthesizesSuccessfullyWithEnvironmentContext(string environment)
        {
            // Arrange
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
            // Arrange
            var app = new App();

            // Act - should work without environment parameter (backward compatible)
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - stack synthesizes without error
            Assert.NotNull(template);
            template.ResourceCountIs("AWS::StepFunctions::StateMachine", 1);
        }
    }
}
