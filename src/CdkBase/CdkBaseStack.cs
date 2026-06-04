using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.StepFunctions;
using Amazon.CDK.AWS.StepFunctions.Tasks;
using Constructs;

namespace CdkBase
{
    public class CdkBaseStack : Stack
    {
        internal CdkBaseStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // Input S3 Bucket for raw sleep audio uploads
            var inputBucket = new Bucket(this, "SleepAudioInputBucket", new BucketProps
            {
                Encryption = BucketEncryption.KMS_MANAGED,
                Versioned = true,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                EventBridgeEnabled = true,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // Output S3 Bucket for processed sleep audio
            new Bucket(this, "SleepAudioOutputBucket", new BucketProps
            {
                Encryption = BucketEncryption.KMS_MANAGED,
                Versioned = true,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // DynamoDB Metadata Table for tracking audio processing status
            var metadataTable = new Table(this, "SleepAudioMetadataTable", new TableProps
            {
                PartitionKey = new Attribute { Name = "audioId", Type = AttributeType.STRING },
                BillingMode = BillingMode.PAY_PER_REQUEST,
                Encryption = TableEncryption.AWS_MANAGED,
                PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification { PointInTimeRecoveryEnabled = true },
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // EventBridge Rule to detect new objects in the input bucket
            var rule = new Rule(this, "AudioUploadRule", new RuleProps
            {
                EventPattern = new EventPattern
                {
                    Source = new[] { "aws.s3" },
                    DetailType = new[] { "Object Created" },
                    Detail = new Dictionary<string, object>
                    {
                        { "bucket", new Dictionary<string, object>
                            {
                                { "name", new[] { inputBucket.BucketName } }
                            }
                        }
                    }
                }
            });

            // CloudWatch Log Group for state machine execution logs
            var logGroup = new LogGroup(this, "StateMachineLogGroup", new Amazon.CDK.AWS.Logs.LogGroupProps
            {
                Retention = RetentionDays.TWO_WEEKS,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // DynamoDB PutItem task - Write initial metadata record
            var writeInitialMetadata = new DynamoPutItem(this, "WriteInitialMetadata", new DynamoPutItemProps
            {
                Table = metadataTable,
                Item = new Dictionary<string, DynamoAttributeValue>
                {
                    { "audioId", DynamoAttributeValue.FromString(JsonPath.StringAt("$.detail.object.key")) },
                    { "status", DynamoAttributeValue.FromString("PROCESSING") },
                    { "inputBucket", DynamoAttributeValue.FromString(JsonPath.StringAt("$.detail.bucket.name")) },
                    { "inputKey", DynamoAttributeValue.FromString(JsonPath.StringAt("$.detail.object.key")) },
                    { "createdAt", DynamoAttributeValue.FromString(JsonPath.StringAt("$$.State.EnteredTime")) }
                },
                ResultPath = "$.dynamodbResult"
            });

            // Polly SynthesizeSpeech task using AWS SDK integration
            var pollyTask = new CustomState(this, "SynthesizeSpeech", new CustomStateProps
            {
                StateJson = new Dictionary<string, object>
                {
                    { "Type", "Task" },
                    { "Resource", "arn:aws:states:::aws-sdk:polly:synthesizeSpeech" },
                    { "Parameters", new Dictionary<string, object>
                        {
                            { "OutputFormat", "mp3" },
                            { "Text", "Welcome to the sleep audio pipeline. This is a placeholder for synthesized speech content." },
                            { "VoiceId", "Joanna" }
                        }
                    },
                    { "ResultPath", "$.pollyResult" }
                }
            });

            // DynamoDB UpdateItem task - Update status to COMPLETED
            var updateStatusCompleted = new DynamoUpdateItem(this, "UpdateStatusCompleted", new DynamoUpdateItemProps
            {
                Table = metadataTable,
                Key = new Dictionary<string, DynamoAttributeValue>
                {
                    { "audioId", DynamoAttributeValue.FromString(JsonPath.StringAt("$.detail.object.key")) }
                },
                ExpressionAttributeValues = new Dictionary<string, DynamoAttributeValue>
                {
                    { ":status", DynamoAttributeValue.FromString("COMPLETED") },
                    { ":updatedAt", DynamoAttributeValue.FromString(JsonPath.StringAt("$$.State.EnteredTime")) }
                },
                UpdateExpression = "SET #s = :status, updatedAt = :updatedAt",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    { "#s", "status" }
                },
                ResultPath = "$.updateResult"
            });

            // DynamoDB UpdateItem task - Update status to FAILED (error handler)
            var updateStatusFailed = new DynamoUpdateItem(this, "UpdateStatusFailed", new DynamoUpdateItemProps
            {
                Table = metadataTable,
                Key = new Dictionary<string, DynamoAttributeValue>
                {
                    { "audioId", DynamoAttributeValue.FromString(JsonPath.StringAt("$.detail.object.key")) }
                },
                ExpressionAttributeValues = new Dictionary<string, DynamoAttributeValue>
                {
                    { ":status", DynamoAttributeValue.FromString("FAILED") },
                    { ":updatedAt", DynamoAttributeValue.FromString(JsonPath.StringAt("$$.State.EnteredTime")) }
                },
                UpdateExpression = "SET #s = :status, updatedAt = :updatedAt",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    { "#s", "status" }
                },
                ResultPath = "$.updateResult"
            });

            // Chain: WriteInitialMetadata -> SynthesizeSpeech -> UpdateStatusCompleted
            // With Catch on the chain transitioning to UpdateStatusFailed
            var chain = Chain.Start(writeInitialMetadata)
                .Next(pollyTask)
                .Next(updateStatusCompleted);

            // Add error handling - catch all errors and transition to UpdateStatusFailed
            writeInitialMetadata.AddCatch(updateStatusFailed, new CatchProps
            {
                Errors = new[] { "States.ALL" },
                ResultPath = "$.error"
            });

            pollyTask.AddCatch(updateStatusFailed, new CatchProps
            {
                Errors = new[] { "States.ALL" },
                ResultPath = "$.error"
            });

            updateStatusCompleted.AddCatch(updateStatusFailed, new CatchProps
            {
                Errors = new[] { "States.ALL" },
                ResultPath = "$.error"
            });

            // Step Functions State Machine for sleep audio processing pipeline
            var stateMachine = new StateMachine(this, "SleepAudioPipelineStateMachine", new StateMachineProps
            {
                DefinitionBody = DefinitionBody.FromChainable(chain),
                Logs = new LogOptions
                {
                    Destination = logGroup,
                    Level = LogLevel.ALL,
                    IncludeExecutionData = true
                },
                TracingEnabled = true
            });

            // Grant Polly permissions to the state machine execution role
            stateMachine.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "polly:SynthesizeSpeech" },
                Resources = new[] { "*" }
            }));

            // Grant DynamoDB write permissions to the state machine
            metadataTable.GrantWriteData(stateMachine);

            // Wire EventBridge rule to target the state machine
            rule.AddTarget(new SfnStateMachine(stateMachine));
        }
    }
}
