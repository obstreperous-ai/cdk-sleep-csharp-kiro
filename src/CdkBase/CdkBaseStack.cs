using System.Collections.Generic;
using System.IO;
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.KMS;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
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

            // Lambda function for audio processing (metadata enrichment and validation)
            var lambdaAssetPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(GetSourceFilePath()), "lambda", "process_audio"));
            var processorFunction = new Function(this, "SleepAudioProcessorFunction", new FunctionProps
            {
                Runtime = Runtime.PYTHON_3_12,
                Handler = "index.handler",
                Code = Code.FromAsset(lambdaAssetPath),
                Timeout = Duration.Seconds(30),
                Environment = new Dictionary<string, string>
                {
                    { "TABLE_NAME", metadataTable.TableName },
                    { "INPUT_BUCKET_NAME", inputBucket.BucketName }
                }
            });

            // Grant the Lambda DynamoDB read/write access
            metadataTable.GrantReadWriteData(processorFunction);

            // LambdaInvoke task for ProcessAudio step in the state machine
            var processAudioTask = new LambdaInvoke(this, "ProcessAudio", new LambdaInvokeProps
            {
                LambdaFunction = processorFunction,
                ResultPath = "$.processAudioResult"
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
                    { ":updatedAt", DynamoAttributeValue.FromString(JsonPath.StringAt("$$.State.EnteredTime")) },
                    { ":errorInfo", DynamoAttributeValue.FromString(JsonPath.StringAt("$.error.Cause")) }
                },
                UpdateExpression = "SET #s = :status, updatedAt = :updatedAt, errorInfo = :errorInfo",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    { "#s", "status" }
                },
                ResultPath = "$.updateResult"
            });

            // SNS Topic for pipeline completion notifications
            var completedTopic = new Topic(this, "SleepAudioPipelineCompleted", new TopicProps
            {
                MasterKey = Amazon.CDK.AWS.KMS.Alias.FromAliasName(this, "SnsCompletedKey", "alias/aws/sns")
            });

            // SNS Topic for pipeline failure notifications
            var failedTopic = new Topic(this, "SleepAudioPipelineFailed", new TopicProps
            {
                MasterKey = Amazon.CDK.AWS.KMS.Alias.FromAliasName(this, "SnsFailedKey", "alias/aws/sns")
            });

            // SNS Publish task for success notification
            var publishSuccess = new SnsPublish(this, "PublishSuccessNotification", new SnsPublishProps
            {
                Topic = completedTopic,
                Message = TaskInput.FromObject(new Dictionary<string, object>
                {
                    { "audioId", JsonPath.StringAt("$.detail.object.key") },
                    { "status", "COMPLETED" },
                    { "timestamp", JsonPath.StringAt("$$.State.EnteredTime") }
                }),
                ResultPath = "$.snsResult"
            });

            // SNS Publish task for failure notification
            var publishFailure = new SnsPublish(this, "PublishFailureNotification", new SnsPublishProps
            {
                Topic = failedTopic,
                Message = TaskInput.FromObject(new Dictionary<string, object>
                {
                    { "audioId", JsonPath.StringAt("$.detail.object.key") },
                    { "status", "FAILED" },
                    { "error", JsonPath.StringAt("$.error.Cause") },
                    { "timestamp", JsonPath.StringAt("$$.State.EnteredTime") }
                }),
                ResultPath = "$.snsFailResult"
            });

            // Wire failure path: UpdateStatusFailed -> PublishFailureNotification
            updateStatusFailed.Next(publishFailure);

            // ValidateInput Choice state - checks file extension for supported formats
            var validateInput = new Choice(this, "ValidateInput");

            // Pass state to inject synthetic error payload when validation fails via Default path
            var validationFailedPass = new Pass(this, "ValidationFailed", new PassProps
            {
                Result = Result.FromObject(new Dictionary<string, object>
                {
                    { "Error", "ValidationError" },
                    { "Cause", "Unsupported file extension" }
                }),
                ResultPath = "$.error"
            });
            validationFailedPass.Next(updateStatusFailed);

            // Define the processing chain after validation
            var validProcessingChain = processAudioTask
                .Next(pollyTask)
                .Next(updateStatusCompleted)
                .Next(publishSuccess);

            // ValidateInput routes valid file extensions to processing, invalid to failure
            validateInput
                .When(Condition.Or(
                    Condition.StringMatches("$.detail.object.key", "*.mp3"),
                    Condition.StringMatches("$.detail.object.key", "*.wav"),
                    Condition.StringMatches("$.detail.object.key", "*.ogg"),
                    Condition.StringMatches("$.detail.object.key", "*.txt"),
                    Condition.StringMatches("$.detail.object.key", "*.MP3"),
                    Condition.StringMatches("$.detail.object.key", "*.WAV"),
                    Condition.StringMatches("$.detail.object.key", "*.OGG"),
                    Condition.StringMatches("$.detail.object.key", "*.TXT")
                ), validProcessingChain)
                .Otherwise(validationFailedPass);

            // Chain: WriteInitialMetadata -> ValidateInput (Choice) -> [valid] ProcessAudio -> SynthesizeSpeech -> UpdateStatusCompleted -> PublishSuccessNotification
            var chain = Chain.Start(writeInitialMetadata)
                .Next(validateInput);

            // Add error handling - catch all errors and transition to UpdateStatusFailed
            writeInitialMetadata.AddCatch(updateStatusFailed, new CatchProps
            {
                Errors = new[] { "States.ALL" },
                ResultPath = "$.error"
            });

            processAudioTask.AddCatch(updateStatusFailed, new CatchProps
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

            publishSuccess.AddCatch(updateStatusFailed, new CatchProps
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

            // Grant SNS publish permissions to the state machine
            completedTopic.GrantPublish(stateMachine);
            failedTopic.GrantPublish(stateMachine);

            // Wire EventBridge rule to target the state machine
            rule.AddTarget(new SfnStateMachine(stateMachine));
        }

        private static string GetSourceFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        {
            return path;
        }
    }
}
