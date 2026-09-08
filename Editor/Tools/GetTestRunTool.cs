using McpUnity.Services;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Polls the SessionState-backed registry for a Unity test run.
    /// </summary>
    public class GetTestRunTool : McpToolBase
    {
        private readonly ITestRunnerService _testRunnerService;

        public GetTestRunTool(ITestRunnerService testRunnerService)
        {
            Name = "get_test_run";
            Description = "Gets a Unity test run by GUID runId, or the most recent run when runId is omitted. Running responses expose expectedArtifactPath with artifactExists:false; artifactPath appears only after validated XML exists. Completed statistics persist as bounded SessionState metadata and result rows are rebuilt from the artifact. Only one run may be tracked because Unity callbacks have no run GUID; a second RunStarted invalidates the record as untrusted. The active lock ends on RunFinished or is released as stale after 24 hours. Owned NUnit XML artifacts are retained for the 20 most recent runs";
            _testRunnerService = testRunnerService;
        }

        public override JObject Execute(JObject parameters)
        {
            return _testRunnerService.GetTestRun(
                parameters?["runId"]?.ToObject<string>());
        }
    }
}
