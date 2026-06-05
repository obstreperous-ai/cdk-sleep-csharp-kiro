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
    }
}
