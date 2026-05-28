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
        public void Stack_HasNoResources()
        {
            // Arrange - disable CDKMetadata so the stack is truly empty
            var app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    { "aws:cdk:disable-metadata", true }
                }
            });

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - empty stack should have no user-defined resources
            template.ResourceCountIs("AWS::S3::Bucket", 0);
            template.ResourceCountIs("AWS::SQS::Queue", 0);
            template.ResourceCountIs("AWS::Lambda::Function", 0);
        }

        [Fact(Skip = "TDD: implement S3 audio ingestion bucket")]
        public void Stack_HasS3BucketForAudioIngestion()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - once pipeline resources are added, verify S3 bucket exists
            template.ResourceCountIs("AWS::S3::Bucket", 1);
        }
    }
}
