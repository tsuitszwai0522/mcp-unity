using UnityEngine;

namespace McpUnity.Tests
{
    public class SerializedPropertyWriteReferenceHolderAsset : ScriptableObject
    {
        [SerializeField]
        private SerializedPropertyWriteReferences m_References =
            new SerializedPropertyWriteReferences();

        public SerializedPropertyWriteReferences References => m_References;
    }
}
