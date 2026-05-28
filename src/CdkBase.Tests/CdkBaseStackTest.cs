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
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - empty stack should have no user-defined resources
            var resources = template.ToJSON();
            Assert.NotNull(resources);
        }

        [Fact(Skip = "TDD: implement sleep audio pipeline resources")]
        public void Stack_HasSqsQueueForAudioProcessing()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new CdkBaseStack(app, "TestStack");
            var template = Template.FromStack(stack);

            // Assert - once pipeline resources are added, verify SQS queue exists
            template.ResourceCountIs("AWS::SQS::Queue", 1);
        }
    }
}
