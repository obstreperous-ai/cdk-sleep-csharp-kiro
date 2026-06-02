using System.Collections.Generic;
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

            // Assert - verify a bucket exists with SSE-KMS encryption
            template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
            {
                { "BucketEncryption", new Dictionary<string, object>
                    {
                        { "ServerSideEncryptionConfiguration", new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "ServerSideEncryptionByDefault", new Dictionary<string, object>
                                        {
                                            { "SSEAlgorithm", "aws:kms" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        [Fact]
        public void Stack_HasInputBucketWithVersioningEnabled()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify a bucket exists with versioning enabled
            template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
            {
                { "VersioningConfiguration", new Dictionary<string, object>
                    {
                        { "Status", "Enabled" }
                    }
                }
            });
        }

        [Fact]
        public void Stack_HasOutputBucketWithEncryptionAndVersioning()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify a bucket with encryption and versioning exists
            template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
            {
                { "BucketEncryption", new Dictionary<string, object>
                    {
                        { "ServerSideEncryptionConfiguration", new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "ServerSideEncryptionByDefault", new Dictionary<string, object>
                                        {
                                            { "SSEAlgorithm", "aws:kms" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                { "VersioningConfiguration", new Dictionary<string, object>
                    {
                        { "Status", "Enabled" }
                    }
                }
            });
        }

        [Fact]
        public void Stack_BucketsBlockPublicAccess()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - verify a bucket exists with all public access blocked
            template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
            {
                { "PublicAccessBlockConfiguration", new Dictionary<string, object>
                    {
                        { "BlockPublicAcls", true },
                        { "BlockPublicPolicy", true },
                        { "IgnorePublicAcls", true },
                        { "RestrictPublicBuckets", true }
                    }
                }
            });
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
    }
}
