using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace CdkBase.Tests
{
    /// <summary>
    /// Shared fixture that synthesizes the CDK template once for all end-to-end validation tests.
    /// Uses xUnit's IClassFixture pattern to avoid repeated JSII template synthesis (56 times).
    /// </summary>
    public class EndToEndValidationFixture
    {
        private static readonly JsonSerializerOptions RelaxedOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public Template Template { get; }
        public string StateMachineDefinitionJson { get; }

        public EndToEndValidationFixture()
        {
            var app = new App();
            var stack = new CdkBaseStack(app, "TestStack");
            Template = Template.FromStack(stack);

            var stateMachines = Template.FindResources("AWS::StepFunctions::StateMachine");
            StateMachineDefinitionJson = JsonSerializer.Serialize(stateMachines.First().Value, RelaxedOptions);
        }
    }

    /// <summary>
    /// End-to-end validation tests for the Sleep Audio Pipeline.
    /// These tests verify the complete pipeline flow through the synthesized CloudFormation template,
    /// including happy path, error scenarios, retry behavior, Lambda configuration, IAM permissions,
    /// and CloudWatch alarm wiring.
    ///
    /// Note: State machine tests use string-based assertions on the serialized JSON definition.
    /// This is a known trade-off for readability vs resilience to formatting changes, and matches
    /// the pattern used in CdkBaseStackTest.cs.
    /// </summary>
    public class EndToEndValidationTest : IClassFixture<EndToEndValidationFixture>
    {
        private readonly Template _template;
        private readonly string _serialized;

        public EndToEndValidationTest(EndToEndValidationFixture fixture)
        {
            _template = fixture.Template;
            _serialized = fixture.StateMachineDefinitionJson;
        }

        // ================================================================
        // Happy Path Tests
        // ================================================================

        [Fact]
        public void HappyPath_StartAtWriteInitialMetadata()
        {
            Assert.Contains("\\\"StartAt\\\":\\\"WriteInitialMetadata\\\"", _serialized);
        }

        [Fact]
        public void HappyPath_WriteInitialMetadataTransitionsToValidateInput()
        {
            Assert.Contains("\\\"WriteInitialMetadata\\\":{\\\"Next\\\":\\\"ValidateInput\\\"", _serialized);
        }

        [Fact]
        public void HappyPath_ValidateInputRoutesValidFilesToProcessAudio()
        {
            // Choice state routes valid extensions to ProcessAudio
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", _serialized);
        }

        [Fact]
        public void HappyPath_ProcessAudioTransitionsToSynthesizeSpeech()
        {
            Assert.Contains("\\\"ProcessAudio\\\":{\\\"Next\\\":\\\"SynthesizeSpeech\\\"", _serialized);
        }

        [Fact]
        public void HappyPath_SynthesizeSpeechTransitionsToUpdateStatusCompleted()
        {
            Assert.Contains("\\\"SynthesizeSpeech\\\":{\\\"Next\\\":\\\"UpdateStatusCompleted\\\"", _serialized);
        }

        [Fact]
        public void HappyPath_UpdateStatusCompletedTransitionsToPublishSuccessNotification()
        {
            Assert.Contains("\\\"UpdateStatusCompleted\\\":{\\\"Next\\\":\\\"PublishSuccessNotification\\\"", _serialized);
        }

        [Fact]
        public void HappyPath_PublishSuccessNotificationIsTerminal()
        {
            Assert.Contains("\\\"PublishSuccessNotification\\\":{\\\"End\\\":true", _serialized);
        }

        [Fact]
        public void HappyPath_CompleteFlowFromStartToEnd()
        {
            // Verify the entire success chain in sequence
            Assert.Contains("\\\"StartAt\\\":\\\"WriteInitialMetadata\\\"", _serialized);
            Assert.Contains("\\\"WriteInitialMetadata\\\":{\\\"Next\\\":\\\"ValidateInput\\\"", _serialized);
            Assert.Contains("\\\"Next\\\":\\\"ProcessAudio\\\"", _serialized);
            Assert.Contains("\\\"ProcessAudio\\\":{\\\"Next\\\":\\\"SynthesizeSpeech\\\"", _serialized);
            Assert.Contains("\\\"SynthesizeSpeech\\\":{\\\"Next\\\":\\\"UpdateStatusCompleted\\\"", _serialized);
            Assert.Contains("\\\"UpdateStatusCompleted\\\":{\\\"Next\\\":\\\"PublishSuccessNotification\\\"", _serialized);
            Assert.Contains("\\\"PublishSuccessNotification\\\":{\\\"End\\\":true", _serialized);
        }

        [Fact]
        public void HappyPath_WriteInitialMetadataUsesDynamoDbPutItem()
        {
            // WriteInitialMetadata should use DynamoDB PutItem
            var stateIdx = _serialized.IndexOf("\\\"WriteInitialMetadata\\\":{");
            Assert.True(stateIdx >= 0, "WriteInitialMetadata state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1500, _serialized.Length - stateIdx));
            Assert.Contains(":states:::dynamodb:putItem", chunk);
        }

        [Fact]
        public void HappyPath_UpdateStatusCompletedUsesDynamoDbUpdateItem()
        {
            // UpdateStatusCompleted should use DynamoDB UpdateItem
            var stateIdx = _serialized.IndexOf("\\\"UpdateStatusCompleted\\\":{");
            Assert.True(stateIdx >= 0, "UpdateStatusCompleted state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1500, _serialized.Length - stateIdx));
            Assert.Contains(":states:::dynamodb:updateItem", chunk);
        }

        [Fact]
        public void HappyPath_SynthesizeSpeechUsesPollyApi()
        {
            var stateIdx = _serialized.IndexOf("\\\"SynthesizeSpeech\\\":{");
            Assert.True(stateIdx >= 0, "SynthesizeSpeech state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1500, _serialized.Length - stateIdx));
            Assert.Contains("arn:aws:states:::aws-sdk:polly:synthesizeSpeech", chunk);
        }

        [Fact]
        public void HappyPath_ProcessAudioUsesLambdaInvoke()
        {
            var stateIdx = _serialized.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1500, _serialized.Length - stateIdx));
            Assert.Contains(":states:::lambda:invoke", chunk);
        }

        [Fact]
        public void HappyPath_PublishSuccessNotificationUsesSnsPublish()
        {
            var stateIdx = _serialized.IndexOf("\\\"PublishSuccessNotification\\\":{");
            Assert.True(stateIdx >= 0, "PublishSuccessNotification state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1000, _serialized.Length - stateIdx));
            Assert.Contains(":states:::sns:publish", chunk);
        }

        [Fact]
        public void HappyPath_EventBridgeTriggersStateMachine()
        {
            // Verify EventBridge rule exists for S3 Object Created events
            _template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
            {
                { "EventPattern", new Dictionary<string, object>
                    {
                        { "source", new object[] { "aws.s3" } },
                        { "detail-type", new object[] { "Object Created" } }
                    }
                }
            });

            // Verify the rule targets the state machine
            var rules = _template.FindResources("AWS::Events::Rule");
            var ruleJson = JsonSerializer.Serialize(rules.First().Value);
            Assert.Contains("SleepAudioPipelineStateMachine", ruleJson);
        }

        // ================================================================
        // Error Scenario Tests
        // ================================================================

        [Fact]
        public void ErrorPath_InvalidExtensionRoutesToValidationFailed()
        {
            // ValidateInput Choice state defaults to ValidationFailed for unsupported extensions
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", _serialized);
        }

        [Fact]
        public void ErrorPath_ValidationFailedTransitionsToUpdateStatusFailed()
        {
            // ValidationFailed is a Pass state that routes to UpdateStatusFailed
            var stateIdx = _serialized.IndexOf("\\\"ValidationFailed\\\":{");
            Assert.True(stateIdx >= 0, "ValidationFailed state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(500, _serialized.Length - stateIdx));
            Assert.Contains("\\\"Type\\\":\\\"Pass\\\"", chunk);
            Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", chunk);
        }

        [Fact]
        public void ErrorPath_UpdateStatusFailedTransitionsToPublishFailureNotification()
        {
            Assert.Contains("\\\"UpdateStatusFailed\\\":{\\\"Next\\\":\\\"PublishFailureNotification\\\"", _serialized);
        }

        [Fact]
        public void ErrorPath_PublishFailureNotificationIsTerminal()
        {
            Assert.Contains("\\\"PublishFailureNotification\\\":{\\\"End\\\":true", _serialized);
        }

        [Fact]
        public void ErrorPath_CompleteFailureFlowFromValidationToEnd()
        {
            // Complete failure path: ValidateInput -> ValidationFailed -> UpdateStatusFailed -> PublishFailureNotification
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", _serialized);

            var vfIdx = _serialized.IndexOf("\\\"ValidationFailed\\\":{");
            Assert.True(vfIdx >= 0);
            var vfChunk = _serialized.Substring(vfIdx, System.Math.Min(500, _serialized.Length - vfIdx));
            Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", vfChunk);

            Assert.Contains("\\\"UpdateStatusFailed\\\":{\\\"Next\\\":\\\"PublishFailureNotification\\\"", _serialized);
            Assert.Contains("\\\"PublishFailureNotification\\\":{\\\"End\\\":true", _serialized);
        }

        [Fact]
        public void ErrorPath_ValidationFailedContainsErrorPayload()
        {
            // ValidationFailed Pass state injects error info
            Assert.Contains("Unsupported file extension", _serialized);
            Assert.Contains("ValidationError", _serialized);
        }

        [Fact]
        public void ErrorPath_UpdateStatusFailedCapturesErrorInfo()
        {
            // The UpdateStatusFailed state should include errorInfo in its DynamoDB update expression
            var stateIdx = _serialized.IndexOf("\\\"UpdateStatusFailed\\\":{");
            Assert.True(stateIdx >= 0, "UpdateStatusFailed state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(2000, _serialized.Length - stateIdx));
            Assert.Contains("errorInfo", chunk);
        }

        [Fact]
        public void ErrorPath_TaskStatesRouteErrorsToUpdateStatusFailed()
        {
            // All Task states in the success path should have Catch routing to UpdateStatusFailed
            var taskStates = new[] { "WriteInitialMetadata", "ProcessAudio", "SynthesizeSpeech", "UpdateStatusCompleted", "PublishSuccessNotification" };

            foreach (var state in taskStates)
            {
                var stateIdx = _serialized.IndexOf($"\\\"{state}\\\":");
                Assert.True(stateIdx >= 0, $"State {state} not found in definition");

                var chunk = _serialized.Substring(stateIdx, System.Math.Min(1200, _serialized.Length - stateIdx));
                Assert.Contains("States.ALL", chunk);
                Assert.Contains("\\\"Next\\\":\\\"UpdateStatusFailed\\\"", chunk);
            }
        }

        // ================================================================
        // Retry Behavior Tests
        // ================================================================

        [Fact]
        public void Retry_WriteInitialMetadataHasRetryConfiguration()
        {
            var stateIdx = _serialized.IndexOf("\\\"WriteInitialMetadata\\\":{");
            Assert.True(stateIdx >= 0, "WriteInitialMetadata state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1000, _serialized.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("IntervalSeconds", chunk);
            Assert.Contains("MaxAttempts", chunk);
            Assert.Contains("BackoffRate", chunk);
        }

        [Fact]
        public void Retry_ProcessAudioHasRetryConfiguration()
        {
            var stateIdx = _serialized.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1000, _serialized.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("IntervalSeconds", chunk);
            Assert.Contains("MaxAttempts", chunk);
            Assert.Contains("BackoffRate", chunk);
        }

        [Fact]
        public void Retry_SynthesizeSpeechHasRetryConfiguration()
        {
            var stateIdx = _serialized.IndexOf("\\\"SynthesizeSpeech\\\":{");
            Assert.True(stateIdx >= 0, "SynthesizeSpeech state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1000, _serialized.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("IntervalSeconds", chunk);
            Assert.Contains("MaxAttempts", chunk);
            Assert.Contains("BackoffRate", chunk);
        }

        [Fact]
        public void Retry_UpdateStatusCompletedHasRetryConfiguration()
        {
            var stateIdx = _serialized.IndexOf("\\\"UpdateStatusCompleted\\\":{");
            Assert.True(stateIdx >= 0, "UpdateStatusCompleted state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1000, _serialized.Length - stateIdx));
            Assert.Contains("Retry", chunk);
            Assert.Contains("IntervalSeconds", chunk);
            Assert.Contains("MaxAttempts", chunk);
            Assert.Contains("BackoffRate", chunk);
        }

        [Fact]
        public void Retry_AllTaskStatesHaveRetryBeforeCatch()
        {
            // All major Task states should have both Retry and Catch
            var taskStates = new[] { "WriteInitialMetadata", "ProcessAudio", "SynthesizeSpeech", "UpdateStatusCompleted" };

            foreach (var state in taskStates)
            {
                var stateIdx = _serialized.IndexOf($"\\\"{state}\\\":");
                Assert.True(stateIdx >= 0, $"State {state} not found");

                var chunk = _serialized.Substring(stateIdx, System.Math.Min(1200, _serialized.Length - stateIdx));
                Assert.Contains("Retry", chunk);
                Assert.Contains("Catch", chunk);
            }
        }

        // ================================================================
        // ProcessAudio Lambda Error Catches Tests
        // ================================================================

        [Fact]
        public void ProcessAudioCatch_HasLambdaServiceException()
        {
            var stateIdx = _serialized.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1200, _serialized.Length - stateIdx));
            Assert.Contains("Lambda.ServiceException", chunk);
        }

        [Fact]
        public void ProcessAudioCatch_HasLambdaSdkClientException()
        {
            var stateIdx = _serialized.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1200, _serialized.Length - stateIdx));
            Assert.Contains("Lambda.SdkClientException", chunk);
        }

        [Fact]
        public void ProcessAudioCatch_HasStatesAllCatch()
        {
            var stateIdx = _serialized.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1200, _serialized.Length - stateIdx));
            Assert.Contains("States.ALL", chunk);
        }

        [Fact]
        public void ProcessAudioRetry_HasLambdaSpecificErrorsForRetry()
        {
            var stateIdx = _serialized.IndexOf("\\\"ProcessAudio\\\":{");
            Assert.True(stateIdx >= 0, "ProcessAudio state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(1200, _serialized.Length - stateIdx));
            // ProcessAudio should retry on Lambda-specific errors
            Assert.Contains("Lambda.ServiceException", chunk);
            Assert.Contains("Lambda.SdkClientException", chunk);
            Assert.Contains("Lambda.TooManyRequestsException", chunk);
        }

        // ================================================================
        // Lambda Function Configuration Tests
        // ================================================================

        [Fact]
        public void LambdaConfig_HasPython312Runtime()
        {
            _template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object>
            {
                { "Runtime", "python3.12" }
            });
        }

        [Fact]
        public void LambdaConfig_Has30SecondTimeout()
        {
            _template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object>
            {
                { "Timeout", 30 }
            });
        }

        [Fact]
        public void LambdaConfig_Has512MBMemory()
        {
            _template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object>
            {
                { "MemorySize", 512 }
            });
        }

        [Fact]
        public void LambdaConfig_HasXRayTracingActive()
        {
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var tracingMode = properties.GetProperty("TracingConfig").GetProperty("Mode").GetString();
            Assert.Equal("Active", tracingMode);
        }

        [Fact]
        public void LambdaConfig_HasTableNameEnvironmentVariable()
        {
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var envVars = properties.GetProperty("Environment").GetProperty("Variables");
            Assert.True(envVars.TryGetProperty("TABLE_NAME", out _),
                "Lambda missing TABLE_NAME environment variable");
        }

        [Fact]
        public void LambdaConfig_HasInputBucketNameEnvironmentVariable()
        {
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var envVars = properties.GetProperty("Environment").GetProperty("Variables");
            Assert.True(envVars.TryGetProperty("INPUT_BUCKET_NAME", out _),
                "Lambda missing INPUT_BUCKET_NAME environment variable");
        }

        [Fact]
        public void LambdaConfig_HasOutputBucketNameEnvironmentVariable()
        {
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(processorFunction.Value))
                .GetProperty("Properties");

            var envVars = properties.GetProperty("Environment").GetProperty("Variables");
            Assert.True(envVars.TryGetProperty("OUTPUT_BUCKET_NAME", out _),
                "Lambda missing OUTPUT_BUCKET_NAME environment variable");
        }

        [Fact]
        public void LambdaConfig_EnvironmentVariablesReferenceCorrectResources()
        {
            var functions = _template.FindResources("AWS::Lambda::Function");
            var processorFunction = functions.First(f => f.Key.Contains("SleepAudioProcessorFunction"));

            var functionJson = JsonSerializer.Serialize(processorFunction.Value);

            // TABLE_NAME should reference the DynamoDB table (AudioMetadataTable)
            Assert.Contains("AudioMetadataTable", functionJson);
            // INPUT_BUCKET_NAME should reference the input bucket
            Assert.Contains("SleepAudioInputBucket", functionJson);
            // OUTPUT_BUCKET_NAME should reference the output bucket
            Assert.Contains("SleepAudioOutputBucket", functionJson);
        }

        [Fact]
        public void LambdaConfig_HasIndexHandler()
        {
            _template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object>
            {
                { "Handler", "index.handler" }
            });
        }

        // ================================================================
        // IAM Permissions Tests
        // ================================================================

        [Fact]
        public void Permissions_LambdaHasS3ReadOnInputBucket()
        {
            var policies = _template.FindResources("AWS::IAM::Policy");

            // Find a policy that grants s3:GetObject to the Lambda on the input bucket
            var lambdaReadPolicy = policies.First(p =>
            {
                var json = JsonSerializer.Serialize(p.Value);
                return json.Contains("SleepAudioProcessorFunction") && json.Contains("s3:GetObject");
            });

            var policyJson = JsonSerializer.Serialize(lambdaReadPolicy.Value);
            Assert.Contains("s3:GetObject", policyJson);
            Assert.Contains("SleepAudioInputBucket", policyJson);
        }

        [Fact]
        public void Permissions_LambdaHasS3WriteOnOutputBucket()
        {
            var policies = _template.FindResources("AWS::IAM::Policy");

            // Find a policy that grants s3:PutObject to the Lambda on the output bucket
            var lambdaWritePolicy = policies.First(p =>
            {
                var json = JsonSerializer.Serialize(p.Value);
                return json.Contains("SleepAudioProcessorFunction") && json.Contains("s3:PutObject");
            });

            var policyJson = JsonSerializer.Serialize(lambdaWritePolicy.Value);
            Assert.Contains("s3:PutObject", policyJson);
            Assert.Contains("SleepAudioOutputBucket", policyJson);
        }

        [Fact]
        public void Permissions_LambdaHasDynamoDbReadWriteAccess()
        {
            var policies = _template.FindResources("AWS::IAM::Policy");
            var policiesJson = JsonSerializer.Serialize(policies);

            // GrantReadWriteData provides these actions
            Assert.Contains("dynamodb:GetItem", policiesJson);
            Assert.Contains("dynamodb:PutItem", policiesJson);
            Assert.Contains("dynamodb:UpdateItem", policiesJson);
            Assert.Contains("dynamodb:DeleteItem", policiesJson);
            Assert.Contains("dynamodb:Query", policiesJson);
            Assert.Contains("dynamodb:Scan", policiesJson);
            Assert.Contains("dynamodb:BatchGetItem", policiesJson);
            Assert.Contains("dynamodb:BatchWriteItem", policiesJson);
        }

        [Fact]
        public void Permissions_LambdaHasS3GetBucketLocation()
        {
            var policies = _template.FindResources("AWS::IAM::Policy");
            var lambdaReadPolicy = policies.First(p =>
            {
                var json = JsonSerializer.Serialize(p.Value);
                return json.Contains("SleepAudioProcessorFunction") && json.Contains("s3:GetObject");
            });

            var policyJson = JsonSerializer.Serialize(lambdaReadPolicy.Value);
            Assert.Contains("s3:GetBucket", policyJson);
        }

        [Fact]
        public void Permissions_LambdaHasS3AbortMultipartUpload()
        {
            var policies = _template.FindResources("AWS::IAM::Policy");
            var lambdaWritePolicy = policies.First(p =>
            {
                var json = JsonSerializer.Serialize(p.Value);
                return json.Contains("SleepAudioProcessorFunction") && json.Contains("s3:PutObject");
            });

            var policyJson = JsonSerializer.Serialize(lambdaWritePolicy.Value);
            Assert.Contains("s3:Abort", policyJson);
        }

        // ================================================================
        // CloudWatch Alarm Wiring Tests
        // ================================================================

        [Fact]
        public void CloudWatchAlarms_AtLeastTwoAlarmsExist()
        {
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");
            Assert.True(alarms.Count >= 2, $"Expected at least 2 CloudWatch Alarms, found {alarms.Count}");
        }

        [Fact]
        public void CloudWatchAlarms_HaveAlarmActions()
        {
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");
            Assert.True(alarms.Count >= 2);

            foreach (var alarm in alarms)
            {
                var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(alarm.Value))
                    .GetProperty("Properties");

                Assert.True(properties.TryGetProperty("AlarmActions", out var alarmActions),
                    $"Alarm {alarm.Key} is missing AlarmActions");
                Assert.True(alarmActions.GetArrayLength() > 0,
                    $"Alarm {alarm.Key} has empty AlarmActions");
            }
        }

        [Fact]
        public void CloudWatchAlarms_AlarmActionsReferenceSnsFailedTopic()
        {
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");
            Assert.True(alarms.Count >= 2);

            // Verify all alarm actions reference the failed topic (SleepAudioPipelineFailed)
            foreach (var alarm in alarms)
            {
                var alarmJson = JsonSerializer.Serialize(alarm.Value);
                // The alarm actions should reference the Failed SNS topic via Ref
                Assert.Contains("SleepAudioPipelineFailed", alarmJson);
            }
        }

        [Fact]
        public void CloudWatchAlarms_StateMachineFailureAlarmExists()
        {
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");

            // At least one alarm should monitor state machine execution failures
            var alarmsJson = JsonSerializer.Serialize(alarms);
            Assert.Contains("ExecutionsFailed", alarmsJson);
        }

        [Fact]
        public void CloudWatchAlarms_LambdaErrorAlarmExists()
        {
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");

            // At least one alarm should monitor Lambda errors
            var alarmsJson = JsonSerializer.Serialize(alarms);
            Assert.Contains("Errors", alarmsJson);
        }

        [Fact]
        public void CloudWatchAlarms_AlarmsHaveCorrectThreshold()
        {
            var alarms = _template.FindResources("AWS::CloudWatch::Alarm");

            foreach (var alarm in alarms)
            {
                var properties = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(alarm.Value))
                    .GetProperty("Properties");

                var threshold = properties.GetProperty("Threshold").GetDouble();
                Assert.Equal(1, threshold);

                var evaluationPeriods = properties.GetProperty("EvaluationPeriods").GetInt32();
                Assert.Equal(1, evaluationPeriods);
            }
        }

        // ================================================================
        // Input Validation Tests
        // ================================================================

        [Fact]
        public void InputValidation_ValidateInputIsChoiceState()
        {
            var stateIdx = _serialized.IndexOf("\\\"ValidateInput\\\":{");
            Assert.True(stateIdx >= 0, "ValidateInput state not found");

            var chunk = _serialized.Substring(stateIdx, System.Math.Min(500, _serialized.Length - stateIdx));
            Assert.Contains("\\\"Type\\\":\\\"Choice\\\"", chunk);
        }

        [Fact]
        public void InputValidation_ChecksSupportedExtensions()
        {
            // Verify the Choice state checks for all supported extensions
            Assert.Contains("*.mp3", _serialized);
            Assert.Contains("*.wav", _serialized);
            Assert.Contains("*.ogg", _serialized);
            Assert.Contains("*.txt", _serialized);
        }

        [Fact]
        public void InputValidation_ChecksUppercaseExtensions()
        {
            // Verify the Choice state also checks uppercase extensions
            Assert.Contains("*.MP3", _serialized);
            Assert.Contains("*.WAV", _serialized);
            Assert.Contains("*.OGG", _serialized);
            Assert.Contains("*.TXT", _serialized);
        }

        [Fact]
        public void InputValidation_DefaultPathIsValidationFailed()
        {
            // Unsupported extensions fall through to Default -> ValidationFailed
            Assert.Contains("\\\"Default\\\":\\\"ValidationFailed\\\"", _serialized);
        }

        [Fact]
        public void InputValidation_ValidationFailedInjectsErrorResultPath()
        {
            // ValidationFailed uses ResultPath $.error to inject error info
            Assert.Contains("$.error", _serialized);
        }
    }
}
