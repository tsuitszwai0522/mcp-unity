using System.Reflection;
using McpUnity.Resources;
using McpUnity.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace McpUnity.Tests
{
    public class ConsoleLogsServiceTests
    {
        private static readonly MethodInfo OnLogMessageReceived =
            typeof(ConsoleLogsService).GetMethod(
                "OnLogMessageReceived",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private ConsoleLogsService _service;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(OnLogMessageReceived);
            _service = new ConsoleLogsService();
            _service.StopListening();
            AddLog("oldest error", LogType.Error);
            AddLog("warning", LogType.Warning);
            AddLog("exception", LogType.Exception);
            AddLog("newest assert", LogType.Assert);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.StopListening();
        }

        [Test]
        public void GetLogsAsJson_CountsAllFilteredEntriesBeyondRequestedPage()
        {
            JObject result = _service.GetLogsAsJson("error", 0, 1, false);

            Assert.AreEqual(4, result["_totalCount"]?.Value<int>());
            Assert.AreEqual(3, result["_filteredCount"]?.Value<int>());
            Assert.AreEqual(1, result["_returnedCount"]?.Value<int>());
            Assert.AreEqual(1, ((JArray)result["logs"]).Count);
        }

        [Test]
        public void GetConsoleLogsResource_MessageUsesCompleteFilteredCount()
        {
            var resource = new GetConsoleLogsResource(_service);

            JObject result = resource.Fetch(new JObject
            {
                ["logType"] = "error",
                ["offset"] = 0,
                ["limit"] = 1,
                ["includeStackTrace"] = false
            });

            Assert.AreEqual(
                "Retrieved 1 of 3 log entries of type 'error' (offset: 0, limit: 1, includeStackTrace: False, total: 4)",
                result["message"]?.ToString());
        }

        private void AddLog(string message, LogType type)
        {
            OnLogMessageReceived.Invoke(
                _service,
                new object[] { message, string.Empty, type });
        }
    }
}
