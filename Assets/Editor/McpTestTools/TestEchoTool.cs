using System.Linq;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;

namespace McpTestTools
{
    /// <summary>
    /// Test tool that echoes back input parameters. Used to verify batch_execute data pass-through.
    /// </summary>
    public class TestEchoTool : McpToolBase
    {
        public TestEchoTool()
        {
            Name = "test_echo";
            Description = "Echoes back the input parameters. For testing batch_execute data return.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""message"": { ""type"": ""string"", ""description"": ""Message to echo back"" },
                ""number"": { ""type"": ""integer"", ""description"": ""A number to echo back"" }
            },
            ""required"": [""message""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string message = parameters["message"]?.ToString() ?? "";
            int number = parameters["number"]?.ToObject<int>() ?? 0;

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Echo: {message}",
                ["echo"] = new JObject
                {
                    ["message"] = message,
                    ["number"] = number,
                    ["reversed"] = new string(message.ToCharArray().Reverse().ToArray())
                }
            };
        }
    }
}
