using Amazon.CDK;
using Amazon.CDK.Pipelines;
using Constructs;

namespace CdkBase
{
    /// <summary>
    /// Defines the CI/CD pipeline stack using AWS CDK Pipelines.
    /// This stack provisions a CodePipeline that automatically builds, tests, and deploys
    /// the Sleep Audio Processing Pipeline from a GitHub repository via CodeStar Connections.
    /// </summary>
    /// <remarks>
    /// Before deploying this pipeline, you must:
    /// 1. Create a CodeStar Connection in the AWS Console for your GitHub account.
    /// 2. Replace the placeholder connectionArn (via CDK context or hardcoded value) with the real ARN.
    /// 3. Update the repository owner from "owner" to your actual GitHub organization or username.
    /// The placeholder ARN uses a fake account number and will fail at deploy time until configured.
    /// </remarks>
    public class PipelineStack : Stack
    {
        internal PipelineStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // Connection ARN is configurable via CDK context; falls back to a placeholder
            // that intentionally fails at deploy time to remind operators to configure it.
            var connectionArn = (string)this.Node.TryGetContext("connectionArn")
                ?? "arn:aws:codestar-connections:us-east-1:123456789012:connection/placeholder";

            var pipeline = new CodePipeline(this, "SleepAudioPipeline", new CodePipelineProps
            {
                PipelineName = "SleepAudioPipeline",
                Synth = new ShellStep("Synth", new ShellStepProps
                {
                    Input = CodePipelineSource.Connection("owner/cdk-sleep-csharp-kiro", "main",
                        new ConnectionSourceOptions
                        {
                            ConnectionArn = connectionArn
                        }),
                    Commands = new[]
                    {
                        "npm install -g aws-cdk",
                        "dotnet restore src/CdkBase.sln",
                        "dotnet build src/CdkBase.sln",
                        "npx cdk synth"
                    }
                })
            });

            // Add application stage that deploys CdkBaseStack
            pipeline.AddStage(new SleepAudioPipelineStage(this, "Deploy"));
        }
    }

    /// <summary>
    /// CDK Pipeline deployment stage that instantiates the Sleep Audio Processing stack.
    /// </summary>
    internal class SleepAudioPipelineStage : Stage
    {
        internal SleepAudioPipelineStage(Construct scope, string id, IStageProps props = null) : base(scope, id, props)
        {
            new CdkBaseStack(this, "SleepAudioStack");
        }
    }
}
