using System.Collections.Generic;
using System.IO;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
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
    /// <summary>
    /// Defines the Sleep Audio Processing Pipeline infrastructure stack.
    /// This stack provisions an event-driven serverless pipeline that processes audio uploads
    /// through S3, EventBridge, Step Functions, Lambda, DynamoDB, Polly, and SNS,
    /// with full observability via CloudWatch alarms and dashboards.
    /// </summary>
    public class CdkBaseStack : Stack
    {
        /// <summary>
        /// Creates a new instance of the Sleep Audio Processing Pipeline stack.
        /// </summary>
        /// <param name="scope">The construct scope (parent).</param>
        /// <param name="id">The logical ID for this stack.</param>
        /// <param name="props">Optional stack properties (account, region, etc.).</param>
        /// <param name="environment">
        /// The target deployment environment (dev, staging, prod).
        /// Defaults to the CDK context value "environment", or "dev" if not specified.
        /// </param>
        internal CdkBaseStack(Construct scope, string id, IStackProps props = null, string environment = null) : base(scope, id, props)
        {
            // Environment defaults to "dev" if not specified
            var env = environment ?? (string)this.Node.TryGetContext("environment") ?? "dev";

            // Tag the stack with the environment name for multi-environment identification
            Amazon.CDK.Tags.Of(this).Add("Environment", env);

            // Create all infrastructure resources via organized helper methods
            var (inputBucket, outputBucket) = CreateStorageBuckets();
            var metadataTable = CreateMetadataTable();
            var rule = CreateEventBridgeRule(inputBucket);
            var logGroup = CreateLogGroup();
            var (completedTopic, failedTopic) = CreateNotificationTopics();
            var (writeInitialMetadata, processAudioTask, pollyTask, updateStatusCompleted, updateStatusFailed, publishSuccess, publishFailure, processorFunction) =
                CreateProcessingSteps(metadataTable, inputBucket, outputBucket, completedTopic, failedTopic);
            var stateMachine = CreateStateMachine(
                logGroup, writeInitialMetadata, processAudioTask, pollyTask,
                updateStatusCompleted, updateStatusFailed, publishSuccess, publishFailure);
            ConfigureStateMachinePermissions(stateMachine, metadataTable, completedTopic, failedTopic);
            rule.AddTarget(new SfnStateMachine(stateMachine));
            CreateAlarmsAndDashboard(stateMachine, failedTopic, processorFunction);
        }

        // ================================================================
        // Storage Resources
        // ================================================================

        /// <summary>
        /// Creates the input and output S3 buckets with KMS encryption, versioning,
        /// and public access blocked.
        /// </summary>
        private (Bucket inputBucket, Bucket outputBucket) CreateStorageBuckets()
        {
            var inputBucket = new Bucket(this, "SleepAudioInputBucket", new BucketProps
            {
                Encryption = BucketEncryption.KMS_MANAGED,
                Versioned = true,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                EventBridgeEnabled = true,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            var outputBucket = new Bucket(this, "SleepAudioOutputBucket", new BucketProps
            {
                Encryption = BucketEncryption.KMS_MANAGED,
                Versioned = true,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            return (inputBucket, outputBucket);
        }

        // ================================================================
        // Metadata Table
        // ================================================================

        /// <summary>
        /// Creates the DynamoDB metadata table for tracking audio processing status.
        /// Uses on-demand billing, encryption, and point-in-time recovery.
        /// </summary>
        private Table CreateMetadataTable()
        {
            return new Table(this, "SleepAudioMetadataTable", new TableProps
            {
                PartitionKey = new Attribute { Name = "audioId", Type = AttributeType.STRING },
                BillingMode = BillingMode.PAY_PER_REQUEST,
                Encryption = TableEncryption.AWS_MANAGED,
                PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification { PointInTimeRecoveryEnabled = true },
                RemovalPolicy = RemovalPolicy.DESTROY
            });
        }

        // ================================================================
        // EventBridge Rule
        // ================================================================

        /// <summary>
        /// Creates an EventBridge rule to detect new objects uploaded to the input bucket.
        /// Filters by the specific input bucket name and "Object Created" events.
        /// </summary>
        private Rule CreateEventBridgeRule(Bucket inputBucket)
        {
            return new Rule(this, "AudioUploadRule", new RuleProps
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
        }

        // ================================================================
        // Logging
        // ================================================================

        /// <summary>
        /// Creates the CloudWatch Log Group for state machine execution logs.
        /// </summary>
        private LogGroup CreateLogGroup()
        {
            return new LogGroup(this, "StateMachineLogGroup", new Amazon.CDK.AWS.Logs.LogGroupProps
            {
                Retention = RetentionDays.TWO_WEEKS,
                RemovalPolicy = RemovalPolicy.DESTROY
            });
        }

        // ================================================================
        // Notifications
        // ================================================================

        /// <summary>
        /// Creates SNS topics for pipeline completion and failure notifications,
        /// both encrypted with the default AWS-managed SNS KMS key.
        /// </summary>
        private (Topic completedTopic, Topic failedTopic) CreateNotificationTopics()
        {
            var completedTopic = new Topic(this, "SleepAudioPipelineCompleted", new TopicProps
            {
                MasterKey = Amazon.CDK.AWS.KMS.Alias.FromAliasName(this, "SnsCompletedKey", "alias/aws/sns")
            });

            var failedTopic = new Topic(this, "SleepAudioPipelineFailed", new TopicProps
            {
                MasterKey = Amazon.CDK.AWS.KMS.Alias.FromAliasName(this, "SnsFailedKey", "alias/aws/sns")
            });

            return (completedTopic, failedTopic);
        }

        // ================================================================
        // Processing Steps (State Machine Tasks)
        // ================================================================

        /// <summary>
        /// Creates all Step Functions task states for the audio processing pipeline,
        /// including DynamoDB operations, Lambda invocation, Polly synthesis, and SNS notifications.
        /// Also grants necessary IAM permissions to the Lambda function.
        /// </summary>
        private (DynamoPutItem writeInitialMetadata, LambdaInvoke processAudioTask, CustomState pollyTask,
            DynamoUpdateItem updateStatusCompleted, DynamoUpdateItem updateStatusFailed,
            SnsPublish publishSuccess, SnsPublish publishFailure, Function processorFunction)
            CreateProcessingSteps(Table metadataTable, Bucket inputBucket, Bucket outputBucket,
                Topic completedTopic, Topic failedTopic)
        {
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
                    { "ResultPath", "$.pollyResult" },
                    { "Retry", new object[]
                        {
                            new Dictionary<string, object>
                            {
                                { "ErrorEquals", new[] { "States.TaskFailed" } },
                                { "IntervalSeconds", 3 },
                                { "MaxAttempts", 2 },
                                { "BackoffRate", 2.0 }
                            }
                        }
                    }
                }
            });

            // Lambda function for audio processing (metadata enrichment and validation)
            var processorFunction = CreateProcessorFunction(metadataTable, inputBucket, outputBucket);

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

            return (writeInitialMetadata, processAudioTask, pollyTask,
                updateStatusCompleted, updateStatusFailed, publishSuccess, publishFailure, processorFunction);
        }

        /// <summary>
        /// Creates the Lambda function for audio processing with appropriate permissions.
        /// Grants DynamoDB read/write, S3 read on input bucket, and S3 write on output bucket.
        /// </summary>
        private Function CreateProcessorFunction(Table metadataTable, Bucket inputBucket, Bucket outputBucket)
        {
            var lambdaAssetPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(GetSourceFilePath()), "lambda", "process_audio"));
            var processorFunction = new Function(this, "SleepAudioProcessorFunction", new FunctionProps
            {
                Runtime = Runtime.PYTHON_3_12,
                Handler = "index.handler",
                Code = Code.FromAsset(lambdaAssetPath),
                Timeout = Duration.Seconds(30),
                MemorySize = 512,
                Tracing = Tracing.ACTIVE,
                Environment = new Dictionary<string, string>
                {
                    { "TABLE_NAME", metadataTable.TableName },
                    { "INPUT_BUCKET_NAME", inputBucket.BucketName },
                    { "OUTPUT_BUCKET_NAME", outputBucket.BucketName }
                }
            });

            // Grant the Lambda DynamoDB read/write access
            metadataTable.GrantReadWriteData(processorFunction);

            // Grant the Lambda S3 read access on the input bucket (download input files)
            inputBucket.GrantRead(processorFunction);

            // Grant the Lambda S3 write access on the output bucket (upload processed files)
            outputBucket.GrantWrite(processorFunction);

            return processorFunction;
        }

        // ================================================================
        // State Machine
        // ================================================================

        /// <summary>
        /// Assembles and creates the Step Functions state machine with the complete
        /// processing chain, error handling (retry + catch), and logging configuration.
        /// </summary>
        private StateMachine CreateStateMachine(
            LogGroup logGroup,
            DynamoPutItem writeInitialMetadata,
            LambdaInvoke processAudioTask,
            CustomState pollyTask,
            DynamoUpdateItem updateStatusCompleted,
            DynamoUpdateItem updateStatusFailed,
            SnsPublish publishSuccess,
            SnsPublish publishFailure)
        {
            // Add retry to failure path tasks to handle transient errors
            updateStatusFailed.AddRetry(new RetryProps
            {
                Errors = new[] { "States.ALL" },
                Interval = Duration.Seconds(1),
                MaxAttempts = 2,
                BackoffRate = 2.0
            });
            publishFailure.AddRetry(new RetryProps
            {
                Errors = new[] { "States.ALL" },
                Interval = Duration.Seconds(1),
                MaxAttempts = 2,
                BackoffRate = 2.0
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
            ConfigureErrorHandling(writeInitialMetadata, processAudioTask, pollyTask,
                updateStatusCompleted, publishSuccess, updateStatusFailed);

            // Step Functions State Machine for sleep audio processing pipeline
            return new StateMachine(this, "SleepAudioPipelineStateMachine", new StateMachineProps
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
        }

        /// <summary>
        /// Configures retry and catch policies for all task states in the pipeline.
        /// Each task retries on transient errors and catches all errors to route to the failure path.
        /// </summary>
        private void ConfigureErrorHandling(
            DynamoPutItem writeInitialMetadata,
            LambdaInvoke processAudioTask,
            CustomState pollyTask,
            DynamoUpdateItem updateStatusCompleted,
            SnsPublish publishSuccess,
            DynamoUpdateItem updateStatusFailed)
        {
            writeInitialMetadata.AddRetry(new RetryProps
            {
                Errors = new[] { "States.ALL" },
                Interval = Duration.Seconds(1),
                MaxAttempts = 3,
                BackoffRate = 2.0
            });
            writeInitialMetadata.AddCatch(updateStatusFailed, new CatchProps
            {
                Errors = new[] { "States.ALL" },
                ResultPath = "$.error"
            });

            // Add specific error catches BEFORE States.ALL on processAudioTask
            processAudioTask.AddRetry(new RetryProps
            {
                Errors = new[] { "Lambda.ServiceException", "Lambda.SdkClientException", "Lambda.TooManyRequestsException" },
                Interval = Duration.Seconds(2),
                MaxAttempts = 3,
                BackoffRate = 2.0
            });
            processAudioTask.AddCatch(updateStatusFailed, new CatchProps
            {
                Errors = new[] { "Lambda.ServiceException", "Lambda.SdkClientException" },
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

            updateStatusCompleted.AddRetry(new RetryProps
            {
                Errors = new[] { "States.ALL" },
                Interval = Duration.Seconds(1),
                MaxAttempts = 3,
                BackoffRate = 2.0
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
        }

        /// <summary>
        /// Grants the state machine execution role permissions for Polly, DynamoDB, and SNS.
        /// </summary>
        private void ConfigureStateMachinePermissions(
            StateMachine stateMachine, Table metadataTable, Topic completedTopic, Topic failedTopic)
        {
            // Grant Polly permissions to the state machine execution role
            // Resources: * is required because Polly SynthesizeSpeech does not support resource-level permissions
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
        }

        // ================================================================
        // Alarms and Dashboard
        // ================================================================

        /// <summary>
        /// Creates CloudWatch alarms for state machine failures and Lambda errors,
        /// plus an operational dashboard showing execution and performance metrics.
        /// </summary>
        private void CreateAlarmsAndDashboard(StateMachine stateMachine, Topic failedTopic, Function processorFunction)
        {
            // CloudWatch Alarm: State Machine Execution Failures
            var executionFailedMetric = stateMachine.MetricFailed(new Amazon.CDK.AWS.CloudWatch.MetricOptions
            {
                Period = Duration.Minutes(1),
                Statistic = "Sum"
            });
            var smAlarm = new Alarm(this, "StateMachineExecutionFailedAlarm", new AlarmProps
            {
                Metric = executionFailedMetric,
                Threshold = 1,
                EvaluationPeriods = 1,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
                AlarmDescription = "Alarm when state machine execution fails"
            });
            smAlarm.AddAlarmAction(new SnsAction(failedTopic));

            // CloudWatch Alarm: Lambda Function Errors
            var lambdaErrorMetric = processorFunction.MetricErrors(new Amazon.CDK.AWS.CloudWatch.MetricOptions
            {
                Period = Duration.Minutes(1),
                Statistic = "Sum"
            });
            var lambdaAlarm = new Alarm(this, "LambdaErrorAlarm", new AlarmProps
            {
                Metric = lambdaErrorMetric,
                Threshold = 1,
                EvaluationPeriods = 1,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
                AlarmDescription = "Alarm when Lambda function errors occur"
            });
            lambdaAlarm.AddAlarmAction(new SnsAction(failedTopic));

            // CloudWatch Dashboard
            new Dashboard(this, "SleepAudioPipelineDashboard", new DashboardProps
            {
                Widgets = new IWidget[][]
                {
                    new IWidget[]
                    {
                        new GraphWidget(new GraphWidgetProps
                        {
                            Title = "State Machine Executions",
                            Left = new IMetric[]
                            {
                                stateMachine.MetricStarted(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
                                stateMachine.MetricSucceeded(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
                                stateMachine.MetricFailed(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" })
                            },
                            Width = 12
                        }),
                        new GraphWidget(new GraphWidgetProps
                        {
                            Title = "Lambda Performance",
                            Left = new IMetric[]
                            {
                                processorFunction.MetricInvocations(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
                                processorFunction.MetricErrors(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" })
                            },
                            Width = 12
                        })
                    }
                }
            });
        }

        // ================================================================
        // Utilities
        // ================================================================

        private static string GetSourceFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        {
            return path;
        }
    }
}
