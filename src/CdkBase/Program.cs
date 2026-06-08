using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CdkBase
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            // Read environment from CDK context (defaults to "dev")
            var environment = (string)app.Node.TryGetContext("environment") ?? "dev";

            new CdkBaseStack(app, "CdkBaseStack", new StackProps
            {
                // If you don't specify 'env', this stack will be environment-agnostic.
                // Account/Region-dependent features and context lookups will not work,
                // but a single synthesized template can be deployed anywhere.

                // Uncomment the next block to specialize this stack for the AWS Account
                // and Region that are implied by the current CLI configuration.
                /*
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }
                */

                // For more information, see https://docs.aws.amazon.com/cdk/latest/guide/environments.html
            }, environment: environment);
            app.Synth();
        }
    }
}
