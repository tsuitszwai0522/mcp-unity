using System;
using System.Linq;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace McpTestTools
{
    /// <summary>
    /// Test tool that returns current time and system info. Used to verify structured data return.
    /// </summary>
    public class TestGetTimeTool : McpToolBase
    {
        public TestGetTimeTool()
        {
            Name = "test_get_time";
            Description = "Returns current time and Unity system info. For testing dynamic tool discovery.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""format"": { ""type"": ""string"", ""description"": ""Time format string (e.g. HH:mm:ss)"", ""default"": ""yyyy-MM-dd HH:mm:ss"" }
            }
        }");

        public override JObject Execute(JObject parameters)
        {
            string format = parameters["format"]?.ToString() ?? "yyyy-MM-dd HH:mm:ss";
            var now = DateTime.Now;

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Current time: {now.ToString(format)}",
                ["time"] = new JObject
                {
                    ["formatted"] = now.ToString(format),
                    ["utc"] = DateTime.UtcNow.ToString("o"),
                    ["unixTimestamp"] = new DateTimeOffset(now).ToUnixTimeSeconds()
                },
                ["system"] = new JObject
                {
                    ["unityVersion"] = Application.unityVersion,
                    ["platform"] = Application.platform.ToString(),
                    ["systemLanguage"] = Application.systemLanguage.ToString()
                }
            };
        }
    }
}
