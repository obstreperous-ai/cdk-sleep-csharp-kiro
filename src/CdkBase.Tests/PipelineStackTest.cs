using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace CdkBase.Tests
{
    public class PipelineStackTest
    {
        [Fact]
        public void PipelineStack_SynthesizesSuccessfully()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new PipelineStack(app, "TestPipelineStack");
            var template = Template.FromStack(stack);

            // Assert
            Assert.NotNull(template);
        }

        [Fact]
        public void PipelineStack_ContainsCodePipelineResource()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new PipelineStack(app, "TestPipelineStack");
            var template = Template.FromStack(stack);

            // Assert - CDK Pipelines generates a CodePipeline resource
            template.ResourceCountIs("AWS::CodePipeline::Pipeline", 1);
        }

        [Fact]
        public void PipelineStack_ContainsCodeBuildProject()
        {
            // Arrange
            var app = new App();

            // Act
            var stack = new PipelineStack(app, "TestPipelineStack");
            var template = Template.FromStack(stack);

            // Assert - CDK Pipelines generates at least one CodeBuild project for synth
            var codeBuildProjects = template.FindResources("AWS::CodeBuild::Project");
            Assert.True(codeBuildProjects.Count >= 1,
                "PipelineStack should contain at least one CodeBuild project for the synth step");
        }
    }
}
