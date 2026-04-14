using System.Collections.Generic;
using NUnit.Framework;
using McpUnity.Tools;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tests
{
    public class GetGameObjectsByNameToolTests
    {
        private GetGameObjectsByNameTool _tool;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _tool = new GetGameObjectsByNameTool();
            _spawned.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        [Test]
        public void Execute_ReturnsValidationError_WhenNameMissing()
        {
            var result = _tool.Execute(new JObject());
            Assert.IsNotNull(result["error"], "Expected validation error response");
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("name"));
        }

        [Test]
        public void Execute_ReturnsValidationError_WhenLimitBelowOne()
        {
            var result = _tool.Execute(new JObject { ["name"] = "*", ["limit"] = 0 });
            Assert.IsNotNull(result["error"], "Expected validation error response");
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("limit"));
        }

        [Test]
        public void Execute_ReturnsValidationError_WhenLimitNegative()
        {
            // Regression: previously RemoveRange would throw on negative limit.
            var result = _tool.Execute(new JObject { ["name"] = "*", ["limit"] = -5 });
            Assert.IsNotNull(result["error"], "Expected validation error response");
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("limit"));
        }

        [Test]
        public void Execute_ReturnsValidationError_WhenLimitAboveMax()
        {
            var result = _tool.Execute(new JObject { ["name"] = "*", ["limit"] = 5000 });
            Assert.IsNotNull(result["error"], "Expected validation error response");
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("limit"));
        }

        [Test]
        public void Execute_ReturnsValidationError_WhenMaxDepthBelowMinusOne()
        {
            var result = _tool.Execute(new JObject { ["name"] = "*", ["maxDepth"] = -2 });
            Assert.IsNotNull(result["error"], "Expected validation error response");
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("maxDepth"));
        }

        [Test]
        public void Execute_TruncatesMatchesAtLimit()
        {
            for (int i = 0; i < 5; i++)
                Spawn($"GgbnT_Truncate_{i}");

            var result = _tool.Execute(new JObject
            {
                ["name"] = "GgbnT_Truncate_*",
                ["limit"] = 3
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(3, result["count"]?.ToObject<int>());
            Assert.IsTrue(result["truncated"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(3, ((JArray)result["gameObjects"]).Count);
        }

        [Test]
        public void Execute_FindsMatchesByGlob()
        {
            Spawn("GgbnT_Match_Alpha");
            Spawn("GgbnT_Match_Beta");
            Spawn("GgbnT_Other");

            var result = _tool.Execute(new JObject { ["name"] = "GgbnT_Match_*" });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(2, result["count"]?.ToObject<int>());
            Assert.IsFalse(result["truncated"]?.ToObject<bool>() ?? true);
        }

    }
}
