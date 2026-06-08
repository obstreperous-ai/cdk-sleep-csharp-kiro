using Amazon.CDK;
using Amazon.CDK.Pipelines;
using Constructs;

namespace CdkBase
{
    public class PipelineStack : Stack
    {
        internal PipelineStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            var pipeline = new CodePipeline(this, "SleepAudioPipeline", new CodePipelineProps
            {
                PipelineName = "SleepAudioPipeline",
                Synth = new ShellStep("Synth", new ShellStepProps
                {
                    Input = CodePipelineSource.Connection("owner/cdk-sleep-csharp-kiro", "main",
                        new ConnectionSourceOptions
                        {
                            ConnectionArn = "arn:aws:codestar-connections:us-east-1:123456789012:connection/placeholder"
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

    internal class SleepAudioPipelineStage : Stage
    {
        internal SleepAudioPipelineStage(Construct scope, string id, IStageProps props = null) : base(scope, id, props)
        {
            new CdkBaseStack(this, "SleepAudioStack");
        }
    }
}
