using System.Collections.Generic;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace McpUnity.Tests
{
    public class RecompileScriptsToolTests
    {
        [Test]
        public void BuildResponse_ReturnsLimitedLogsAndTruncationMetadata()
        {
            var logs = new List<CompilerMessage>
            {
                CreateMessage("First error", CompilerMessageType.Error),
                CreateMessage("First warning", CompilerMessageType.Warning),
                CreateMessage("Extra info", CompilerMessageType.Info)
            };

            var response = RecompileScriptsTool.BuildResponse(logs, 1, 1, true, 2);

            Assert.IsTrue(response["success"]?.ToObject<bool>() ?? false);
            Assert.AreEqual("text", response["type"]?.ToString());
            Assert.AreEqual(
                "Recompilation completed with 1 error(s) and 1 warning(s) (returnWithLogs: True, logsLimit: 2)",
                response["message"]?.ToString());
            Assert.AreEqual(2, ((JArray)response["logs"]).Count);
            Assert.AreEqual(3, response["totalLogs"]?.ToObject<int>());
            Assert.AreEqual(2, response["returnedLogs"]?.ToObject<int>());
            Assert.IsTrue(response["truncated"]?.ToObject<bool>() ?? false);
        }

        [Test]
        public void BuildResponse_ReportsTruncatedWhenExistingLogsAreNotRequested()
        {
            var logs = new List<CompilerMessage>
            {
                CreateMessage("Warning", CompilerMessageType.Warning)
            };

            var response = RecompileScriptsTool.BuildResponse(logs, 1, 0, false, 100);

            Assert.AreEqual(0, ((JArray)response["logs"]).Count);
            Assert.AreEqual(1, response["totalLogs"]?.ToObject<int>());
            Assert.AreEqual(0, response["returnedLogs"]?.ToObject<int>());
            Assert.IsTrue(response["truncated"]?.ToObject<bool>() ?? false);
        }

        [Test]
        public void BuildResponse_DoesNotReportTruncatedWhenNoLogsExist()
        {
            var response = RecompileScriptsTool.BuildResponse(
                new List<CompilerMessage>(), 0, 0, false, 100);

            Assert.AreEqual(0, response["totalLogs"]?.ToObject<int>());
            Assert.AreEqual(0, response["returnedLogs"]?.ToObject<int>());
            Assert.IsFalse(response["truncated"]?.ToObject<bool>() ?? true);
        }

        private static CompilerMessage CreateMessage(string message, CompilerMessageType type)
        {
            return new CompilerMessage
            {
                message = message,
                type = type
            };
        }
    }
}
