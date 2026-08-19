using System;
using UnityEngine;

namespace McpUnity.Tests
{
    public class ConverterFidelityScriptableObject : ScriptableObject
    {
        public int number = 9;
        public UnityEngine.Object reference;
        public ConverterFidelityFlags flags;
    }
}
