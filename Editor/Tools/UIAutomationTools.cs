using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using Unity.EditorCoroutines.Editor;

namespace McpUnity.Tools
{
    #region Utilities

    /// <summary>
    /// Utility class for UI automation tools providing shared functionality
    /// </summary>
    public static class UIAutomationUtils
    {
        /// <summary>
        /// Check if the application is in Play Mode, returns error JObject if not
        /// </summary>
        public static JObject RequirePlayMode()
        {
            if (!Application.isPlaying)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "This tool requires Play Mode. Enter Play Mode first (use set_editor_state to enter Play Mode).",
                    "play_mode_required"
                );
            }
            return null;
        }

        /// <summary>
        /// Check if an EventSystem exists in the scene, returns error JObject if not
        /// </summary>
        public static JObject RequireEventSystem()
        {
            if (EventSystem.current == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "No EventSystem found in scene. Ensure an EventSystem exists.",
                    "no_event_system"
                );
            }
            return null;
        }

        /// <summary>
        /// Find a GameObject by instanceId or objectPath (Play Mode compatible)
        /// </summary>
        public static JObject FindGameObject(int? instanceId, string objectPath, out GameObject gameObject, out string identifierInfo)
        {
            gameObject = null;
            identifierInfo = "";

            if (instanceId.HasValue)
            {
                gameObject = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                identifierInfo = $"instance ID {instanceId.Value}";
            }
            else if (!string.IsNullOrEmpty(objectPath))
            {
                gameObject = GameObject.Find(objectPath);
                identifierInfo = $"path '{objectPath}'";
            }
            else
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'instanceId' or 'objectPath' must be provided.",
                    "validation_error"
                );
            }

            if (gameObject == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject not found using {identifierInfo}.",
                    "not_found_error"
                );
            }

            return null; // Success
        }

        /// <summary>
        /// Extract state information from a Selectable component
        /// </summary>
        public static JObject ExtractSelectableState(Selectable selectable)
        {
            var state = new JObject
            {
                ["interactable"] = selectable.interactable
            };

            switch (selectable)
            {
                case Button _:
                    state["componentType"] = "Button";
                    // Try to find child text
                    var buttonText = selectable.GetComponentInChildren<Text>();
                    if (buttonText != null)
                        state["text"] = buttonText.text;
                    else
                    {
                        string tmpText = GetTMPText(selectable.gameObject);
                        if (tmpText != null)
                            state["text"] = tmpText;
                    }
                    break;

                case Toggle toggle:
                    state["componentType"] = "Toggle";
                    state["isOn"] = toggle.isOn;
                    var toggleLabel = toggle.GetComponentInChildren<Text>();
                    if (toggleLabel != null)
                        state["label"] = toggleLabel.text;
                    else
                    {
                        string tmpLabel = GetTMPText(toggle.gameObject);
                        if (tmpLabel != null)
                            state["label"] = tmpLabel;
                    }
                    break;

                case InputField inputField:
                    state["componentType"] = "InputField";
                    state["text"] = inputField.text;
                    if (inputField.placeholder is Text placeholderText)
                        state["placeholder"] = placeholderText.text;
                    break;

                case Slider slider:
                    state["componentType"] = "Slider";
                    state["value"] = slider.value;
                    state["minValue"] = slider.minValue;
                    state["maxValue"] = slider.maxValue;
                    state["wholeNumbers"] = slider.wholeNumbers;
                    break;

                case Dropdown dropdown:
                    state["componentType"] = "Dropdown";
                    state["value"] = dropdown.value;
                    if (dropdown.captionText != null)
                        state["selectedText"] = dropdown.captionText.text;
                    var options = new JArray();
                    foreach (var option in dropdown.options)
                        options.Add(option.text);
                    state["options"] = options;
                    break;

                case Scrollbar scrollbar:
                    state["componentType"] = "Scrollbar";
                    state["value"] = scrollbar.value;
                    state["size"] = scrollbar.size;
                    break;

                default:
                    state["componentType"] = selectable.GetType().Name;
                    break;
            }

            return state;
        }

        /// <summary>
        /// Extract state from TMP_InputField via reflection
        /// </summary>
        public static JObject ExtractTMPInputFieldState(Component tmpInputField)
        {
            if (tmpInputField == null) return null;
            Type type = tmpInputField.GetType();
            var state = new JObject
            {
                ["componentType"] = "TMP_InputField"
            };

            var textProp = type.GetProperty("text");
            if (textProp != null)
                state["text"] = textProp.GetValue(tmpInputField) as string ?? "";

            var interactableProp = type.GetProperty("interactable");
            if (interactableProp != null)
                state["interactable"] = (bool)interactableProp.GetValue(tmpInputField);

            // Try to get placeholder text
            var placeholderField = type.GetProperty("placeholder");
            if (placeholderField != null)
            {
                var placeholder = placeholderField.GetValue(tmpInputField) as Component;
                if (placeholder != null)
                {
                    string placeholderText = GetTMPText(placeholder.gameObject) ?? GetLegacyText(placeholder.gameObject);
                    if (placeholderText != null)
                        state["placeholder"] = placeholderText;
                }
            }

            return state;
        }

        /// <summary>
        /// Extract state from TMP_Dropdown via reflection
        /// </summary>
        public static JObject ExtractTMPDropdownState(Component tmpDropdown)
        {
            if (tmpDropdown == null) return null;
            Type type = tmpDropdown.GetType();
            var state = new JObject
            {
                ["componentType"] = "TMP_Dropdown"
            };

            var valueProp = type.GetProperty("value");
            if (valueProp != null)
                state["value"] = (int)valueProp.GetValue(tmpDropdown);

            var interactableProp = type.GetProperty("interactable");
            if (interactableProp != null)
                state["interactable"] = (bool)interactableProp.GetValue(tmpDropdown);

            // Try to get caption text
            var captionTextProp = type.GetProperty("captionText");
            if (captionTextProp != null)
            {
                var captionText = captionTextProp.GetValue(tmpDropdown) as Component;
                if (captionText != null)
                {
                    string text = GetTMPText(captionText.gameObject);
                    if (text != null)
                        state["selectedText"] = text;
                }
            }

            return state;
        }

        /// <summary>
        /// Get TMP_Text content from a GameObject or its children via reflection
        /// </summary>
        public static string GetTMPText(GameObject go)
        {
            Type tmpTextType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (tmpTextType == null) return null;

            Component tmpText = go.GetComponentInChildren(tmpTextType);
            if (tmpText == null) return null;

            var textProp = tmpTextType.GetProperty("text");
            return textProp?.GetValue(tmpText) as string;
        }

        /// <summary>
        /// Get legacy Text content from a GameObject or its children
        /// </summary>
        public static string GetLegacyText(GameObject go)
        {
            Text text = go.GetComponentInChildren<Text>();
            return text?.text;
        }

        /// <summary>
        /// Get the display text from a GameObject.
        /// For InputField/TMP_InputField: returns the .text property (actual content).
        /// For other elements: tries TMP_Text first, then legacy Text in children.
        /// </summary>
        public static string GetDisplayText(GameObject go)
        {
            // Check for InputField first — GetComponentInChildren<Text>() would return
            // the Placeholder child, not the actual input text.
            InputField inputField = go.GetComponent<InputField>();
            if (inputField != null)
                return inputField.text;

            // Check for TMP_InputField via reflection
            if (UGUIToolUtils.IsTMProAvailable())
            {
                Type tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                if (tmpInputType != null)
                {
                    Component tmpInput = go.GetComponent(tmpInputType);
                    if (tmpInput != null)
                    {
                        var textProp = tmpInputType.GetProperty("text");
                        if (textProp != null)
                            return textProp.GetValue(tmpInput) as string;
                    }
                }
            }

            return GetTMPText(go) ?? GetLegacyText(go);
        }

        /// <summary>
        /// Get hierarchy path of a GameObject
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return null;
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        /// <summary>
        /// Get the screen-space center position of a UI GameObject
        /// </summary>
        public static Vector2 GetScreenCenter(GameObject go)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                Canvas canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    Vector3[] corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) / 2f;
                    if (cam != null)
                        return RectTransformUtility.WorldToScreenPoint(cam, center);
                    else
                        return center;
                }
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Scan for all interactable elements under a root transform
        /// </summary>
        public static List<JObject> ScanInteractableElements(
            Transform root,
            HashSet<string> filter,
            bool includeNonInteractable)
        {
            var results = new List<JObject>();

            // Find all Selectables
            Selectable[] selectables;
            if (root != null)
                selectables = root.GetComponentsInChildren<Selectable>(includeNonInteractable);
            else
                selectables = UnityEngine.Object.FindObjectsByType<Selectable>(
                    includeNonInteractable ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var selectable in selectables)
            {
                if (!includeNonInteractable && !selectable.interactable)
                    continue;
                if (!includeNonInteractable && !selectable.gameObject.activeInHierarchy)
                    continue;

                JObject state = ExtractSelectableState(selectable);
                string componentType = state["componentType"]?.ToString();

                if (filter != null && !filter.Contains(componentType))
                    continue;

                var element = new JObject
                {
                    ["path"] = GetGameObjectPath(selectable.gameObject),
                    ["instanceId"] = selectable.gameObject.GetInstanceID(),
                    ["componentType"] = componentType,
                    ["interactable"] = selectable.interactable,
                    ["active"] = selectable.gameObject.activeInHierarchy,
                    ["state"] = state
                };
                results.Add(element);
            }

            // Build HashSet for O(1) dedup lookups
            var capturedInstanceIds = new HashSet<int>();
            foreach (var element in results)
                capturedInstanceIds.Add((int)element["instanceId"]);

            // Check for TMP_InputField (not a Selectable subclass in some versions)
            if (UGUIToolUtils.IsTMProAvailable())
            {
                Type tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                if (tmpInputType != null)
                {
                    Component[] tmpInputFields;
                    if (root != null)
                        tmpInputFields = root.GetComponentsInChildren(tmpInputType, includeNonInteractable);
                    else
                        tmpInputFields = UnityEngine.Object.FindObjectsByType(tmpInputType,
                            includeNonInteractable ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                            FindObjectsSortMode.None) as Component[];

                    if (tmpInputFields != null)
                    {
                        foreach (var tmpInput in tmpInputFields)
                        {
                            // Skip if already captured as Selectable
                            if (capturedInstanceIds.Contains(tmpInput.gameObject.GetInstanceID()))
                                continue;

                            if (filter != null && !filter.Contains("TMP_InputField"))
                                continue;

                            JObject state = ExtractTMPInputFieldState(tmpInput);
                            bool interactable = state["interactable"]?.ToObject<bool>() ?? true;

                            if (!includeNonInteractable && !interactable)
                                continue;
                            if (!includeNonInteractable && !tmpInput.gameObject.activeInHierarchy)
                                continue;

                            var element = new JObject
                            {
                                ["path"] = GetGameObjectPath(tmpInput.gameObject),
                                ["instanceId"] = tmpInput.gameObject.GetInstanceID(),
                                ["componentType"] = "TMP_InputField",
                                ["interactable"] = interactable,
                                ["active"] = tmpInput.gameObject.activeInHierarchy,
                                ["state"] = state
                            };
                            results.Add(element);
                            capturedInstanceIds.Add(tmpInput.gameObject.GetInstanceID());
                        }
                    }
                }

                // Check for TMP_Dropdown
                Type tmpDropdownType = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
                if (tmpDropdownType != null)
                {
                    Component[] tmpDropdowns;
                    if (root != null)
                        tmpDropdowns = root.GetComponentsInChildren(tmpDropdownType, includeNonInteractable);
                    else
                        tmpDropdowns = UnityEngine.Object.FindObjectsByType(tmpDropdownType,
                            includeNonInteractable ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                            FindObjectsSortMode.None) as Component[];

                    if (tmpDropdowns != null)
                    {
                        foreach (var tmpDropdown in tmpDropdowns)
                        {
                            if (capturedInstanceIds.Contains(tmpDropdown.gameObject.GetInstanceID()))
                                continue;

                            if (filter != null && !filter.Contains("TMP_Dropdown"))
                                continue;

                            JObject state = ExtractTMPDropdownState(tmpDropdown);
                            bool interactable = state["interactable"]?.ToObject<bool>() ?? true;

                            if (!includeNonInteractable && !interactable)
                                continue;
                            if (!includeNonInteractable && !tmpDropdown.gameObject.activeInHierarchy)
                                continue;

                            var element = new JObject
                            {
                                ["path"] = GetGameObjectPath(tmpDropdown.gameObject),
                                ["instanceId"] = tmpDropdown.gameObject.GetInstanceID(),
                                ["componentType"] = "TMP_Dropdown",
                                ["interactable"] = interactable,
                                ["active"] = tmpDropdown.gameObject.activeInHierarchy,
                                ["state"] = state
                            };
                            results.Add(element);
                            capturedInstanceIds.Add(tmpDropdown.gameObject.GetInstanceID());
                        }
                    }
                }
            }

            // Check for ScrollRect (not a Selectable)
            if (filter == null || filter.Contains("ScrollRect"))
            {
                ScrollRect[] scrollRects;
                if (root != null)
                    scrollRects = root.GetComponentsInChildren<ScrollRect>(includeNonInteractable);
                else
                    scrollRects = UnityEngine.Object.FindObjectsByType<ScrollRect>(
                        includeNonInteractable ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var scrollRect in scrollRects)
                {
                    if (capturedInstanceIds.Contains(scrollRect.gameObject.GetInstanceID()))
                        continue;

                    if (!includeNonInteractable && !scrollRect.gameObject.activeInHierarchy)
                        continue;

                    var state = new JObject
                    {
                        ["componentType"] = "ScrollRect",
                        ["horizontal"] = scrollRect.horizontal,
                        ["vertical"] = scrollRect.vertical
                    };

                    var element = new JObject
                    {
                        ["path"] = GetGameObjectPath(scrollRect.gameObject),
                        ["instanceId"] = scrollRect.gameObject.GetInstanceID(),
                        ["componentType"] = "ScrollRect",
                        ["interactable"] = true,
                        ["active"] = scrollRect.gameObject.activeInHierarchy,
                        ["state"] = state
                    };
                    results.Add(element);
                }
            }

            return results;
        }
    }

    #endregion

    #region GetInteractableElementsTool

    /// <summary>
    /// Tool to scan the scene for all interactable UI elements
    /// </summary>
    public class GetInteractableElementsTool : McpToolBase
    {
        public GetInteractableElementsTool()
        {
            Name = "get_interactable_elements";
            Description = "Scans the scene for all interactable UI elements (Button, Toggle, InputField, Slider, Dropdown, ScrollRect, etc.) and returns their paths, types, and current states. Requires Play Mode.";
        }

        public override JObject Execute(JObject parameters)
        {
            // Require Play Mode
            var playModeError = UIAutomationUtils.RequirePlayMode();
            if (playModeError != null) return playModeError;

            // Parse parameters
            string rootPath = parameters?["rootPath"]?.ToObject<string>();
            bool includeNonInteractable = parameters?["includeNonInteractable"]?.ToObject<bool>() ?? false;

            // Parse filter
            HashSet<string> filter = null;
            JArray filterArray = parameters?["filter"] as JArray;
            if (filterArray != null && filterArray.Count > 0)
            {
                filter = new HashSet<string>();
                foreach (var item in filterArray)
                    filter.Add(item.ToString());
            }

            // Find root transform
            Transform root = null;
            if (!string.IsNullOrEmpty(rootPath))
            {
                GameObject rootObj = GameObject.Find(rootPath);
                if (rootObj == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Root GameObject not found at path '{rootPath}'.",
                        "not_found_error"
                    );
                }
                root = rootObj.transform;
            }

            // Scan
            List<JObject> elements = UIAutomationUtils.ScanInteractableElements(root, filter, includeNonInteractable);

            var elementsArray = new JArray();
            foreach (var element in elements)
                elementsArray.Add(element);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {elements.Count} interactable element(s)",
                ["elements"] = elementsArray,
                ["count"] = elements.Count
            };
        }
    }

    #endregion

    #region SimulatePointerClickTool

    /// <summary>
    /// Tool to simulate a full pointer click event sequence on a UI element
    /// </summary>
    public class SimulatePointerClickTool : McpToolBase
    {
        public SimulatePointerClickTool()
        {
            Name = "simulate_pointer_click";
            Description = "Simulates a full pointer click event sequence (PointerEnter → PointerDown → PointerUp → PointerClick → PointerExit) on a UI element. Requires Play Mode.";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteCoroutine(parameters, tcs));
        }

        private IEnumerator ExecuteCoroutine(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Require Play Mode
            var playModeError = UIAutomationUtils.RequirePlayMode();
            if (playModeError != null)
            {
                tcs.TrySetResult(playModeError);
                yield break;
            }

            // Require EventSystem
            var eventSystemError = UIAutomationUtils.RequireEventSystem();
            if (eventSystemError != null)
            {
                tcs.TrySetResult(eventSystemError);
                yield break;
            }

            // Find target
            int? instanceId = parameters?["instanceId"]?.ToObject<int?>();
            string objectPath = parameters?["objectPath"]?.ToObject<string>();
            var findError = UIAutomationUtils.FindGameObject(instanceId, objectPath, out GameObject target, out string identifierInfo);
            if (findError != null)
            {
                tcs.TrySetResult(findError);
                yield break;
            }

            // Validate: active in hierarchy
            if (!target.activeInHierarchy)
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject {identifierInfo} is not active in hierarchy.",
                    "validation_error"
                ));
                yield break;
            }

            // Validate: has Selectable and is interactable
            Selectable selectable = target.GetComponentInParent<Selectable>();
            if (selectable != null && !selectable.interactable)
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Selectable on {identifierInfo} is not interactable.",
                    "validation_error"
                ));
                yield break;
            }

            // Create PointerEventData
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = UIAutomationUtils.GetScreenCenter(target),
                button = PointerEventData.InputButton.Left,
                pointerPress = target,
                pointerEnter = target
            };

            var dispatched = new List<string>();

            // Frame 1: PointerEnter → PointerDown
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerEnterHandler);
            dispatched.Add("PointerEnter");
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerDownHandler);
            dispatched.Add("PointerDown");

            yield return null; // Next frame

            // Frame 2: PointerUp → PointerClick
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerUpHandler);
            dispatched.Add("PointerUp");
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerClickHandler);
            dispatched.Add("PointerClick");

            yield return null; // Next frame

            // Frame 3: PointerExit
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerExitHandler);
            dispatched.Add("PointerExit");

            // Build response with state after click
            var dispatchedArray = new JArray();
            foreach (var evt in dispatched)
                dispatchedArray.Add(evt);

            var result = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully clicked {UIAutomationUtils.GetGameObjectPath(target)}",
                ["targetPath"] = UIAutomationUtils.GetGameObjectPath(target),
                ["eventsDispatched"] = dispatchedArray
            };

            // Add state after click
            if (selectable != null)
            {
                result["stateAfter"] = UIAutomationUtils.ExtractSelectableState(selectable);
            }

            tcs.TrySetResult(result);
        }
    }

    #endregion

    #region SimulateInputFieldTool

    /// <summary>
    /// Tool to fill text into an InputField or TMP_InputField
    /// </summary>
    public class SimulateInputFieldTool : McpToolBase
    {
        public SimulateInputFieldTool()
        {
            Name = "simulate_input_field";
            Description = "Fills text into an InputField or TMP_InputField, triggering onValueChanged and optionally onEndEdit/onSubmit events. Requires Play Mode.";
        }

        public override JObject Execute(JObject parameters)
        {
            // Require Play Mode
            var playModeError = UIAutomationUtils.RequirePlayMode();
            if (playModeError != null) return playModeError;

            // Find target
            int? instanceId = parameters?["instanceId"]?.ToObject<int?>();
            string objectPath = parameters?["objectPath"]?.ToObject<string>();
            var findError = UIAutomationUtils.FindGameObject(instanceId, objectPath, out GameObject target, out string identifierInfo);
            if (findError != null) return findError;

            // Get text parameter
            string text = parameters?["text"]?.ToObject<string>();
            if (text == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'text' not provided.",
                    "validation_error"
                );
            }

            string mode = parameters?["mode"]?.ToObject<string>() ?? "replace";
            bool submitAfter = parameters?["submitAfter"]?.ToObject<bool>() ?? true;

            // Try legacy InputField first
            InputField inputField = target.GetComponent<InputField>();
            if (inputField != null)
            {
                if (!inputField.interactable)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"InputField at {identifierInfo} is not interactable.",
                        "validation_error"
                    );
                }

                string previousText = inputField.text;
                string newText = mode == "append" ? previousText + text : text;

                inputField.text = newText;
                inputField.onValueChanged?.Invoke(newText);

                if (submitAfter)
                {
                    inputField.onEndEdit?.Invoke(newText);
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully set text on InputField at {UIAutomationUtils.GetGameObjectPath(target)}",
                    ["targetPath"] = UIAutomationUtils.GetGameObjectPath(target),
                    ["inputFieldType"] = "InputField",
                    ["previousText"] = previousText,
                    ["currentText"] = newText,
                    ["submitted"] = submitAfter
                };
            }

            // Try TMP_InputField via reflection
            if (UGUIToolUtils.IsTMProAvailable())
            {
                Type tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                if (tmpInputType != null)
                {
                    Component tmpInputField = target.GetComponent(tmpInputType);
                    if (tmpInputField != null)
                    {
                        // Check interactable via reflection
                        var interactableProp = tmpInputType.GetProperty("interactable");
                        if (interactableProp != null && !(bool)interactableProp.GetValue(tmpInputField))
                        {
                            return McpUnitySocketHandler.CreateErrorResponse(
                                $"TMP_InputField at {identifierInfo} is not interactable.",
                                "validation_error"
                            );
                        }

                        var textProp = tmpInputType.GetProperty("text");
                        if (textProp != null)
                        {
                            string previousText = textProp.GetValue(tmpInputField) as string ?? "";
                            string newText = mode == "append" ? previousText + text : text;

                            textProp.SetValue(tmpInputField, newText);

                            // Trigger onValueChanged
                            var onValueChangedField = tmpInputType.GetProperty("onValueChanged");
                            if (onValueChangedField != null)
                            {
                                var onValueChanged = onValueChangedField.GetValue(tmpInputField);
                                if (onValueChanged != null)
                                {
                                    var invokeMethod = onValueChanged.GetType().GetMethod("Invoke", new Type[] { typeof(string) });
                                    invokeMethod?.Invoke(onValueChanged, new object[] { newText });
                                }
                            }

                            // Trigger onEndEdit if submitAfter
                            if (submitAfter)
                            {
                                var onEndEditField = tmpInputType.GetProperty("onEndEdit");
                                if (onEndEditField != null)
                                {
                                    var onEndEdit = onEndEditField.GetValue(tmpInputField);
                                    if (onEndEdit != null)
                                    {
                                        var invokeMethod = onEndEdit.GetType().GetMethod("Invoke", new Type[] { typeof(string) });
                                        invokeMethod?.Invoke(onEndEdit, new object[] { newText });
                                    }
                                }

                                var onSubmitField = tmpInputType.GetProperty("onSubmit");
                                if (onSubmitField != null)
                                {
                                    var onSubmit = onSubmitField.GetValue(tmpInputField);
                                    if (onSubmit != null)
                                    {
                                        var invokeMethod = onSubmit.GetType().GetMethod("Invoke", new Type[] { typeof(string) });
                                        invokeMethod?.Invoke(onSubmit, new object[] { newText });
                                    }
                                }
                            }

                            return new JObject
                            {
                                ["success"] = true,
                                ["type"] = "text",
                                ["message"] = $"Successfully set text on TMP_InputField at {UIAutomationUtils.GetGameObjectPath(target)}",
                                ["targetPath"] = UIAutomationUtils.GetGameObjectPath(target),
                                ["inputFieldType"] = "TMP_InputField",
                                ["previousText"] = previousText,
                                ["currentText"] = newText,
                                ["submitted"] = submitAfter
                            };
                        }
                    }
                }
            }

            return McpUnitySocketHandler.CreateErrorResponse(
                $"No InputField or TMP_InputField component found on {identifierInfo}.",
                "component_error"
            );
        }
    }

    #endregion

    #region GetUIElementStateTool

    /// <summary>
    /// Tool to query the runtime state of a single UI element
    /// </summary>
    public class GetUIElementStateTool : McpToolBase
    {
        public GetUIElementStateTool()
        {
            Name = "get_ui_element_state";
            Description = "Queries the runtime state of a single UI element including component states, RectTransform info, and display text. Works in both Edit and Play Mode.";
        }

        public override JObject Execute(JObject parameters)
        {
            // Find target (no Play Mode requirement)
            int? instanceId = parameters?["instanceId"]?.ToObject<int?>();
            string objectPath = parameters?["objectPath"]?.ToObject<string>();
            var findError = UIAutomationUtils.FindGameObject(instanceId, objectPath, out GameObject target, out string identifierInfo);
            if (findError != null) return findError;

            string path = UIAutomationUtils.GetGameObjectPath(target);

            var result = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"UI element state for {path}",
                ["path"] = path,
                ["instanceId"] = target.GetInstanceID(),
                ["active"] = target.activeSelf,
                ["activeInHierarchy"] = target.activeInHierarchy
            };

            // Collect UGUI component states
            var components = new JObject();

            // Selectable components
            Selectable selectable = target.GetComponent<Selectable>();
            if (selectable != null)
            {
                JObject state = UIAutomationUtils.ExtractSelectableState(selectable);
                string componentType = state["componentType"]?.ToString() ?? selectable.GetType().Name;
                components[componentType] = state;
            }

            // TMP_InputField
            if (UGUIToolUtils.IsTMProAvailable())
            {
                Type tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                if (tmpInputType != null)
                {
                    Component tmpInput = target.GetComponent(tmpInputType);
                    if (tmpInput != null && selectable == null) // Avoid duplicate if already captured
                    {
                        JObject state = UIAutomationUtils.ExtractTMPInputFieldState(tmpInput);
                        if (state != null)
                            components["TMP_InputField"] = state;
                    }
                }

                Type tmpDropdownType = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
                if (tmpDropdownType != null)
                {
                    Component tmpDropdown = target.GetComponent(tmpDropdownType);
                    if (tmpDropdown != null && selectable == null)
                    {
                        JObject state = UIAutomationUtils.ExtractTMPDropdownState(tmpDropdown);
                        if (state != null)
                            components["TMP_Dropdown"] = state;
                    }
                }
            }

            // ScrollRect
            ScrollRect scrollRect = target.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                components["ScrollRect"] = new JObject
                {
                    ["horizontal"] = scrollRect.horizontal,
                    ["vertical"] = scrollRect.vertical,
                    ["normalizedPosition"] = new JObject
                    {
                        ["x"] = scrollRect.horizontalNormalizedPosition,
                        ["y"] = scrollRect.verticalNormalizedPosition
                    }
                };
            }

            // Image
            Image image = target.GetComponent<Image>();
            if (image != null)
            {
                components["Image"] = new JObject
                {
                    ["color"] = $"({image.color.r:F2}, {image.color.g:F2}, {image.color.b:F2}, {image.color.a:F2})",
                    ["raycastTarget"] = image.raycastTarget,
                    ["sprite"] = image.sprite != null ? image.sprite.name : null
                };
            }

            // CanvasGroup
            CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                components["CanvasGroup"] = new JObject
                {
                    ["alpha"] = canvasGroup.alpha,
                    ["interactable"] = canvasGroup.interactable,
                    ["blocksRaycasts"] = canvasGroup.blocksRaycasts
                };
            }

            result["components"] = components;

            // RectTransform
            RectTransform rect = target.GetComponent<RectTransform>();
            if (rect != null)
            {
                result["rectTransform"] = new JObject
                {
                    ["anchoredPosition"] = new JObject { ["x"] = rect.anchoredPosition.x, ["y"] = rect.anchoredPosition.y },
                    ["sizeDelta"] = new JObject { ["x"] = rect.sizeDelta.x, ["y"] = rect.sizeDelta.y },
                    ["anchorMin"] = new JObject { ["x"] = rect.anchorMin.x, ["y"] = rect.anchorMin.y },
                    ["anchorMax"] = new JObject { ["x"] = rect.anchorMax.x, ["y"] = rect.anchorMax.y },
                    ["pivot"] = new JObject { ["x"] = rect.pivot.x, ["y"] = rect.pivot.y }
                };
            }

            // Display text (child Text/TMP_Text)
            string displayText = UIAutomationUtils.GetDisplayText(target);
            if (displayText != null)
                result["displayText"] = displayText;

            return result;
        }
    }

    #endregion

    #region WaitForConditionTool

    /// <summary>
    /// Tool to wait for a specified condition to be met on a UI element
    /// </summary>
    public class WaitForConditionTool : McpToolBase
    {
        private const float MaxTimeout = 30f;
        private const float MinPollInterval = 0.05f;

        public WaitForConditionTool()
        {
            Name = "wait_for_condition";
            Description = "Waits for a specified condition (active, inactive, exists, not_exists, interactable, text_equals, text_contains, component_enabled) on a GameObject with a configurable timeout. Requires Play Mode.";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteCoroutine(parameters, tcs));
        }

        private IEnumerator ExecuteCoroutine(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Require Play Mode
            var playModeError = UIAutomationUtils.RequirePlayMode();
            if (playModeError != null)
            {
                tcs.TrySetResult(playModeError);
                yield break;
            }

            // Parse parameters
            string objectPath = parameters?["objectPath"]?.ToObject<string>();
            string condition = parameters?["condition"]?.ToObject<string>();
            string value = parameters?["value"]?.ToObject<string>();
            float timeout = parameters?["timeout"]?.ToObject<float>() ?? 10f;
            float pollInterval = parameters?["pollInterval"]?.ToObject<float>() ?? 0.1f;

            if (string.IsNullOrEmpty(objectPath))
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'objectPath' not provided.",
                    "validation_error"
                ));
                yield break;
            }

            if (string.IsNullOrEmpty(condition))
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'condition' not provided.",
                    "validation_error"
                ));
                yield break;
            }

            // Validate condition type
            var validConditions = new HashSet<string>
            {
                "active", "inactive", "exists", "not_exists",
                "interactable", "text_equals", "text_contains", "component_enabled"
            };
            if (!validConditions.Contains(condition))
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid condition '{condition}'. Valid conditions: {string.Join(", ", validConditions)}",
                    "validation_error"
                ));
                yield break;
            }

            // Validate value parameter for conditions that need it
            if ((condition == "text_equals" || condition == "text_contains" || condition == "component_enabled")
                && string.IsNullOrEmpty(value))
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Condition '{condition}' requires the 'value' parameter.",
                    "validation_error"
                ));
                yield break;
            }

            // Clamp timeout and pollInterval
            timeout = Mathf.Clamp(timeout, 0.1f, MaxTimeout);
            pollInterval = Mathf.Max(pollInterval, MinPollInterval);

            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            bool conditionMet = false;

            while (elapsed < timeout)
            {
                // Check if we exited Play Mode during wait
                if (!Application.isPlaying)
                {
                    tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                        "Play Mode exited during wait.",
                        "play_mode_required"
                    ));
                    yield break;
                }

                conditionMet = CheckCondition(objectPath, condition, value);
                if (conditionMet)
                    break;

                // Wait for pollInterval
                float waitStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - waitStart < pollInterval)
                    yield return null;

                elapsed = Time.realtimeSinceStartup - startTime;
            }

            // Build final state
            var finalState = new JObject();
            GameObject obj = GameObject.Find(objectPath);
            if (obj != null)
            {
                finalState["active"] = obj.activeSelf;
                finalState["activeInHierarchy"] = obj.activeInHierarchy;
                string displayText = UIAutomationUtils.GetDisplayText(obj);
                if (displayText != null)
                    finalState["displayText"] = displayText;
            }
            else
            {
                finalState["exists"] = false;
            }

            if (conditionMet)
            {
                tcs.TrySetResult(new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Condition '{condition}' met on '{objectPath}' after {elapsed:F2}s",
                    ["condition"] = condition,
                    ["objectPath"] = objectPath,
                    ["elapsed"] = Math.Round(elapsed, 2),
                    ["finalState"] = finalState
                });
            }
            else
            {
                tcs.TrySetResult(new JObject
                {
                    ["success"] = false,
                    ["error"] = new JObject
                    {
                        ["type"] = "timeout_error",
                        ["message"] = $"Timeout after {timeout:F1}s waiting for '{condition}' on '{objectPath}'"
                    },
                    ["condition"] = condition,
                    ["objectPath"] = objectPath,
                    ["elapsed"] = Math.Round((double)timeout, 2),
                    ["finalState"] = finalState
                });
            }
        }

        private static bool CheckCondition(string objectPath, string condition, string value)
        {
            GameObject obj = GameObject.Find(objectPath);

            switch (condition)
            {
                case "exists":
                    return obj != null;

                case "not_exists":
                    return obj == null;

                case "active":
                    return obj != null && obj.activeInHierarchy;

                case "inactive":
                    return obj != null && !obj.activeInHierarchy;

                case "interactable":
                    if (obj == null) return false;
                    Selectable sel = obj.GetComponent<Selectable>();
                    return sel != null && sel.interactable;

                case "text_equals":
                    if (obj == null) return false;
                    string text1 = UIAutomationUtils.GetDisplayText(obj);
                    return text1 != null && text1 == value;

                case "text_contains":
                    if (obj == null) return false;
                    string text2 = UIAutomationUtils.GetDisplayText(obj);
                    return text2 != null && text2.Contains(value);

                case "component_enabled":
                    if (obj == null || string.IsNullOrEmpty(value)) return false;
                    Component comp = obj.GetComponent(value);
                    if (comp is Behaviour behaviour)
                        return behaviour.enabled;
                    return comp != null;

                default:
                    return false;
            }
        }
    }

    #endregion

    #region SimulateDragTool

    /// <summary>
    /// Tool to simulate a drag gesture on a UI element
    /// </summary>
    public class SimulateDragTool : McpToolBase
    {
        public SimulateDragTool()
        {
            Name = "simulate_drag";
            Description = "Simulates a drag gesture on a UI element with a full event sequence (PointerDown → BeginDrag → Drag (N frames) → EndDrag → PointerUp). Supports delta (pixel offset) or targetPath (drag to another element). Requires Play Mode.";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteCoroutine(parameters, tcs));
        }

        private IEnumerator ExecuteCoroutine(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Require Play Mode
            var playModeError = UIAutomationUtils.RequirePlayMode();
            if (playModeError != null)
            {
                tcs.TrySetResult(playModeError);
                yield break;
            }

            // Require EventSystem
            var eventSystemError = UIAutomationUtils.RequireEventSystem();
            if (eventSystemError != null)
            {
                tcs.TrySetResult(eventSystemError);
                yield break;
            }

            // Find source object
            int? instanceId = parameters?["instanceId"]?.ToObject<int?>();
            string objectPath = parameters?["objectPath"]?.ToObject<string>();
            var findError = UIAutomationUtils.FindGameObject(instanceId, objectPath, out GameObject source, out string identifierInfo);
            if (findError != null)
            {
                tcs.TrySetResult(findError);
                yield break;
            }

            if (!source.activeInHierarchy)
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject {identifierInfo} is not active in hierarchy.",
                    "validation_error"
                ));
                yield break;
            }

            // Determine delta
            Vector2 totalDelta;
            string targetPath = parameters?["targetPath"]?.ToObject<string>();
            JObject deltaObj = parameters?["delta"] as JObject;

            Vector2 startPos = UIAutomationUtils.GetScreenCenter(source);

            if (deltaObj != null)
            {
                float dx = deltaObj["x"]?.ToObject<float>() ?? 0f;
                float dy = deltaObj["y"]?.ToObject<float>() ?? 0f;
                totalDelta = new Vector2(dx, dy);
            }
            else if (!string.IsNullOrEmpty(targetPath))
            {
                GameObject targetObj = GameObject.Find(targetPath);
                if (targetObj == null)
                {
                    tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                        $"Target GameObject not found at path '{targetPath}'.",
                        "not_found_error"
                    ));
                    yield break;
                }
                Vector2 endPos = UIAutomationUtils.GetScreenCenter(targetObj);
                totalDelta = endPos - startPos;
            }
            else
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'delta' or 'targetPath' must be provided.",
                    "validation_error"
                ));
                yield break;
            }

            int steps = parameters?["steps"]?.ToObject<int>() ?? 5;
            steps = Mathf.Clamp(steps, 1, 60);
            float duration = parameters?["duration"]?.ToObject<float>() ?? 0.3f;
            duration = Mathf.Clamp(duration, 0.05f, 5f);
            float stepDelay = duration / steps;

            Vector2 endPosition = startPos + totalDelta;

            // Create PointerEventData
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = startPos,
                button = PointerEventData.InputButton.Left,
                pointerDrag = source,
                pointerPress = source
            };

            // Frame 0: PointerEnter + PointerDown + InitializePotentialDrag
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.initializePotentialDrag);
            yield return null;

            // Frame 1: BeginDrag
            pointerData.dragging = true;
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.beginDragHandler);
            yield return null;

            // Frames 2..N: Drag with interpolation
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector2 currentPos = Vector2.Lerp(startPos, endPosition, t);
                pointerData.position = currentPos;
                pointerData.delta = (currentPos - (Vector2.Lerp(startPos, endPosition, (float)(i - 1) / steps)));
                ExecuteEvents.Execute(source, pointerData, ExecuteEvents.dragHandler);

                // Wait for stepDelay
                float waitStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - waitStart < stepDelay)
                    yield return null;
            }

            // Final frame: EndDrag + PointerUp + PointerExit + Drop
            pointerData.position = endPosition;
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerExitHandler);

            // Check for drop target
            string dropReceiver = null;
            if (!string.IsNullOrEmpty(targetPath))
            {
                GameObject dropTarget = GameObject.Find(targetPath);
                if (dropTarget != null)
                {
                    pointerData.pointerDrag = source;
                    ExecuteEvents.ExecuteHierarchy(dropTarget, pointerData, ExecuteEvents.dropHandler);
                    dropReceiver = UIAutomationUtils.GetGameObjectPath(dropTarget);
                }
            }

            tcs.TrySetResult(new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully dragged {UIAutomationUtils.GetGameObjectPath(source)}" +
                              (dropReceiver != null ? $" to {dropReceiver}" : $" by ({totalDelta.x}, {totalDelta.y})"),
                ["sourcePath"] = UIAutomationUtils.GetGameObjectPath(source),
                ["startPosition"] = new JObject { ["x"] = startPos.x, ["y"] = startPos.y },
                ["endPosition"] = new JObject { ["x"] = endPosition.x, ["y"] = endPosition.y },
                ["totalDelta"] = new JObject { ["x"] = totalDelta.x, ["y"] = totalDelta.y },
                ["steps"] = steps,
                ["dropReceiver"] = dropReceiver
            });
        }
    }

    #endregion
}
