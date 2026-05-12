using Amazon.CDK;
using Constructs;

namespace CdkBase
{
    public class CdkBaseStack : Stack
    {
        internal CdkBaseStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // The code that defines your stack goes here
        }
    }
}
