using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Threading.Tasks;

namespace McpUnity.Tests
{
    public class AddPackageUrlTests
    {
        private const string RepositoryUrl =
            "https://github.com/hadashiA/VContainer.git";

        [Test]
        public void BranchAndPathPutPathQueryBeforeBranchFragment()
        {
            Assert.AreEqual(
                RepositoryUrl + "?path=VContainer/Assets/VContainer#1.17.0",
                AddPackageTool.BuildGitHubPackageUrl(
                    RepositoryUrl,
                    "1.17.0",
                    "VContainer/Assets/VContainer"));
        }

        [Test]
        public void PathOnlyUsesPathQuery()
        {
            Assert.AreEqual(
                RepositoryUrl + "?path=Packages/src",
                AddPackageTool.BuildGitHubPackageUrl(
                    RepositoryUrl,
                    null,
                    "Packages/src"));
        }

        [Test]
        public void BranchOnlyUsesFragment()
        {
            Assert.AreEqual(
                RepositoryUrl + "#main",
                AddPackageTool.BuildGitHubPackageUrl(RepositoryUrl, "main", null));
        }

        [Test]
        public void RepositoryOnlyIsReturnedUnchanged()
        {
            Assert.AreEqual(
                RepositoryUrl,
                AddPackageTool.BuildGitHubPackageUrl(RepositoryUrl, null, null));
        }

        [Test]
        public void GitSuffixIsPreserved()
        {
            Assert.AreEqual(
                RepositoryUrl + "?path=spine-csharp/src#4.2",
                AddPackageTool.BuildGitHubPackageUrl(
                    RepositoryUrl,
                    "4.2",
                    "spine-csharp/src"));
        }

        [Test]
        public void LeadingPathSlashesAreStripped()
        {
            Assert.AreEqual(
                RepositoryUrl + "?path=Packages/src#main",
                AddPackageTool.BuildGitHubPackageUrl(
                    RepositoryUrl,
                    "main",
                    "///Packages/src"));
        }

        [TestCase("?path=Packages/src")]
        [TestCase("#main")]
        public void RepositoryUrlWithEmbeddedQueryOrFragmentReturnsValidationError(string suffix)
        {
            JObject result = ExecuteGitHub(RepositoryUrl + suffix, null);

            AssertErrorType(result, "validation_error");
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("branch"));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("path"));
        }

        [TestCase("Packages/src?variant=runtime")]
        [TestCase("Packages/src#fragment")]
        [TestCase("Packages/A&path=Packages/Evil")]
        public void PathWithQueryFragmentOrAmpersandReturnsValidationError(string path)
        {
            JObject result = ExecuteGitHub(RepositoryUrl, path);

            AssertErrorType(result, "validation_error");
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("path"));
        }

        private static JObject ExecuteGitHub(string repositoryUrl, string path)
        {
            var completionSource = new TaskCompletionSource<JObject>();
            var parameters = new JObject
            {
                ["source"] = "github",
                ["repositoryUrl"] = repositoryUrl
            };
            if (path != null)
                parameters["path"] = path;

            new AddPackageTool().ExecuteAsync(
                parameters,
                completionSource);

            Assert.IsTrue(
                completionSource.Task.IsCompleted,
                "Polluted GitHub input must be rejected before Client.Add is called.");
            return completionSource.Task.Result;
        }

        private static void AssertErrorType(JObject result, string expectedType)
        {
            Assert.AreEqual(
                expectedType,
                result["error"]?["type"]?.ToString(),
                result.ToString());
        }
    }
}
