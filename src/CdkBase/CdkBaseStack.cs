using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SQS;
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

            // Stub SQS queue as placeholder target pending Step Functions implementation
            var stubQueue = new Queue(this, "StubProcessingQueue", new QueueProps
            {
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            rule.AddTarget(new SqsQueue(stubQueue));
        }
    }
}
