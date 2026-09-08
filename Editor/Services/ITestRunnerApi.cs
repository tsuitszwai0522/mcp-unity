using System;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace McpUnity.Services
{
    /// <summary>
    /// Injectable subset of Unity's TestRunnerApi used by TestRunnerService.
    /// Unity's own ITestRunnerApi is internal, so the package keeps this seam.
    /// </summary>
    public interface ITestRunnerApi
    {
        string Execute(ExecutionSettings executionSettings);
        void RegisterCallbacks(ICallbacks callbacks);
        void RetrieveTestList(TestMode testMode, Action<ITestAdaptor> callback);
        void SaveResultToFile(ITestResultAdaptor results, string xmlFilePath);
    }

    internal sealed class UnityTestRunnerApi : ITestRunnerApi
    {
        private readonly TestRunnerApi _api;

        public UnityTestRunnerApi()
        {
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
        }

        public string Execute(ExecutionSettings executionSettings)
        {
            return _api.Execute(executionSettings);
        }

        public void RegisterCallbacks(ICallbacks callbacks)
        {
            _api.RegisterCallbacks(callbacks);
        }

        public void RetrieveTestList(TestMode testMode, Action<ITestAdaptor> callback)
        {
            _api.RetrieveTestList(testMode, callback);
        }

        public void SaveResultToFile(ITestResultAdaptor results, string xmlFilePath)
        {
            TestRunnerApi.SaveResultToFile(results, xmlFilePath);
        }
    }
}
