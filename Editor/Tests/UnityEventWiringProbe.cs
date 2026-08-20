using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace McpUnity.Tests
{
    public class UnityEventWiringProbe : MonoBehaviour
    {
        public UnityEvent noArgs = new UnityEvent();
        public UnityEventWiringIntEvent intEvent = new UnityEventWiringIntEvent();
        public List<UnityEventWiringPayload> payloads = new List<UnityEventWiringPayload>();

        public int receivedInt;
        public string receivedString;

        public void ReceiveInt(int value)
        {
            receivedInt = value;
        }

        public void ReceiveString(string value)
        {
            receivedString = value;
        }

        public void ReceiveAmbiguous(int value)
        {
            receivedInt = value;
        }

        public void ReceiveAmbiguous()
        {
            receivedInt = -1;
        }

        public void ReceiveNumber(int value)
        {
            receivedInt = value;
        }

        public void ReceiveNumber(float value)
        {
            receivedInt = (int)value;
        }
    }
}
