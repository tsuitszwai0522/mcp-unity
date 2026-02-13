using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Services;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    #region Utilities

    /// <summary>
    /// Utility class for UGUI tools providing shared functionality
    /// </summary>
    public static class UGUIToolUtils
    {
        /// <summary>
        /// Dictionary of anchor presets mapping preset names to (anchorMin, anchorMax, pivot) tuples
        /// </summary>
        public static readonly Dictionary<string, (Vector2 min, Vector2 max, Vector2 pivot)> AnchorPresets =
            new Dictionary<string, (Vector2, Vector2, Vector2)>
            {
                // Top row
                { "topLeft", (new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1)) },
                { "topCenter", (new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1)) },
                { "topRight", (new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1)) },
                { "topStretch", (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1)) },

                // Middle row
                { "middleLeft", (new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f)) },
                { "middleCenter", (new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)) },
                { "middleRight", (new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f)) },
                { "middleStretch", (new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f)) },

                // Bottom row
                { "bottomLeft", (new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0)) },
                { "bottomCenter", (new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0)) },
                { "bottomRight", (new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0)) },
                { "bottomStretch", (new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0)) },

                // Stretch column
                { "stretchLeft", (new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f)) },
                { "stretchCenter", (new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f)) },
                { "stretchRight", (new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f)) },
                { "stretch", (new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f)) },
            };

        /// <summary>
        /// Apply an anchor preset to a RectTransform
        /// </summary>
        public static void ApplyAnchorPreset(RectTransform rect, string preset)
        {
            if (rect == null || string.IsNullOrEmpty(preset))
                return;

            if (AnchorPresets.TryGetValue(preset, out var values))
            {
                rect.anchorMin = values.min;
                rect.anchorMax = values.max;
                rect.pivot = values.pivot;
            }
        }

        /// <summary>
        /// Ensure an EventSystem exists in the scene, creating one if necessary.
        /// Supports both New Input System and Legacy Input Manager.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            EventSystem eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventSystemGO = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
                eventSystem = eventSystemGO.AddComponent<EventSystem>();

                // Try to add InputSystemUIInputModule (New Input System) first
                Type inputSystemModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputSystemModuleType != null)
                {
                    Undo.AddComponent(eventSystemGO, inputSystemModuleType);
                }
                else
                {
                    // Fallback to StandaloneInputModule (Legacy Input Manager)
                    eventSystemGO.AddComponent<StandaloneInputModule>();
                }
            }
            return eventSystem;
        }

        /// <summary>
        /// Find or create a Canvas in the scene
        /// </summary>
        public static Canvas FindOrCreateCanvas(string path = "Canvas")
        {
            // First try to find an existing canvas at the path
            GameObject existingObj = GameObject.Find(path);
            if (existingObj != null)
            {
                Canvas existingCanvas = existingObj.GetComponent<Canvas>();
                if (existingCanvas != null)
                    return existingCanvas;
            }

            // If not found, try to find any canvas
            Canvas anyCanvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (anyCanvas != null)
                return anyCanvas;

            // Create a new canvas
            GameObject canvasGO = new GameObject(path);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Ensure EventSystem exists
            EnsureEventSystem();

            return canvas;
        }

        /// <summary>
        /// Check if TextMeshPro is available in the project
        /// </summary>
        public static bool IsTMProAvailable()
        {
            return Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") != null;
        }

        /// <summary>
        /// Get hierarchy path of a GameObject
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return null;
            string path = obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = obj.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// Find a GameObject by instance ID or path
        /// </summary>
        public static GameObject FindGameObject(int? instanceId, string objectPath, out string identifier)
        {
            identifier = "unknown";
            GameObject gameObject = null;

            if (instanceId.HasValue)
            {
                gameObject = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                identifier = $"instance ID {instanceId.Value}";
            }
            else if (!string.IsNullOrEmpty(objectPath))
            {
                gameObject = GameObject.Find(objectPath);
                identifier = $"path '{objectPath}'";

                if (gameObject == null)
                {
                    // Try to find using scene hierarchy traversal
                    gameObject = FindGameObjectByPath(objectPath);
                }
                // Fallback: search in Prefab edit mode contents
                if (gameObject == null && PrefabEditingService.IsEditing)
                {
                    gameObject = PrefabEditingService.FindByPath(objectPath);
                }
            }

            return gameObject;
        }

        /// <summary>
        /// Find a GameObject by its hierarchy path
        /// </summary>
        private static GameObject FindGameObjectByPath(string path)
        {
            string[] pathParts = path.Split('/');
            GameObject[] rootGameObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            if (pathParts.Length == 0)
                return null;

            foreach (GameObject rootObj in rootGameObjects)
            {
                if (rootObj.name == pathParts[0])
                {
                    GameObject current = rootObj;
                    for (int i = 1; i < pathParts.Length; i++)
                    {
                        Transform child = current.transform.Find(pathParts[i]);
                        if (child == null)
                            return null;
                        current = child.gameObject;
                    }
                    return current;
                }
            }

            // Fallback: check Prefab edit mode root
            if (PrefabEditingService.IsEditing
                && PrefabEditingService.PrefabRoot.name == pathParts[0])
            {
                GameObject current = PrefabEditingService.PrefabRoot;
                for (int i = 1; i < pathParts.Length; i++)
                {
                    Transform child = current.transform.Find(pathParts[i]);
                    if (child == null)
                        return null;
                    current = child.gameObject;
                }
                return current;
            }

            return null;
        }

        /// <summary>
        /// Get RectTransform info as a JObject
        /// </summary>
        public static JObject GetRectTransformInfo(RectTransform rect)
        {
            if (rect == null)
                return null;

            return new JObject
            {
                ["anchorMin"] = new JObject { ["x"] = rect.anchorMin.x, ["y"] = rect.anchorMin.y },
                ["anchorMax"] = new JObject { ["x"] = rect.anchorMax.x, ["y"] = rect.anchorMax.y },
                ["pivot"] = new JObject { ["x"] = rect.pivot.x, ["y"] = rect.pivot.y },
                ["anchoredPosition"] = new JObject { ["x"] = rect.anchoredPosition.x, ["y"] = rect.anchoredPosition.y },
                ["sizeDelta"] = new JObject { ["x"] = rect.sizeDelta.x, ["y"] = rect.sizeDelta.y },
                ["offsetMin"] = new JObject { ["x"] = rect.offsetMin.x, ["y"] = rect.offsetMin.y },
                ["offsetMax"] = new JObject { ["x"] = rect.offsetMax.x, ["y"] = rect.offsetMax.y },
                ["localPosition"] = new JObject { ["x"] = rect.localPosition.x, ["y"] = rect.localPosition.y, ["z"] = rect.localPosition.z },
                ["localRotation"] = new JObject { ["x"] = rect.localEulerAngles.x, ["y"] = rect.localEulerAngles.y, ["z"] = rect.localEulerAngles.z },
                ["localScale"] = new JObject { ["x"] = rect.localScale.x, ["y"] = rect.localScale.y, ["z"] = rect.localScale.z },
                ["rect"] = new JObject { ["x"] = rect.rect.x, ["y"] = rect.rect.y, ["width"] = rect.rect.width, ["height"] = rect.rect.height }
            };
        }

        /// <summary>
        /// Parse a Vector2 from JObject
        /// </summary>
        public static Vector2 ParseVector2(JObject obj, Vector2 defaultValue = default)
        {
            if (obj == null)
                return defaultValue;
            return new Vector2(
                obj["x"]?.ToObject<float>() ?? defaultValue.x,
                obj["y"]?.ToObject<float>() ?? defaultValue.y
            );
        }

        /// <summary>
        /// Parse a Vector3 from JObject
        /// </summary>
        public static Vector3 ParseVector3(JObject obj, Vector3 defaultValue = default)
        {
            if (obj == null)
                return defaultValue;
            return new Vector3(
                obj["x"]?.ToObject<float>() ?? defaultValue.x,
                obj["y"]?.ToObject<float>() ?? defaultValue.y,
                obj["z"]?.ToObject<float>() ?? defaultValue.z
            );
        }

        /// <summary>
        /// Parse a Color from JObject
        /// </summary>
        public static Color ParseColor(JObject obj, Color defaultValue = default)
        {
            if (obj == null)
                return defaultValue;
            return new Color(
                obj["r"]?.ToObject<float>() ?? defaultValue.r,
                obj["g"]?.ToObject<float>() ?? defaultValue.g,
                obj["b"]?.ToObject<float>() ?? defaultValue.b,
                obj["a"]?.ToObject<float>() ?? defaultValue.a
            );
        }

        /// <summary>
        /// Parse RectOffset from JObject
        /// </summary>
        public static RectOffset ParseRectOffset(JObject obj)
        {
            if (obj == null)
                return new RectOffset();
            return new RectOffset(
                obj["left"]?.ToObject<int>() ?? 0,
                obj["right"]?.ToObject<int>() ?? 0,
                obj["top"]?.ToObject<int>() ?? 0,
                obj["bottom"]?.ToObject<int>() ?? 0
            );
        }
    }

    #endregion

    #region CreateCanvasTool

    /// <summary>
    /// Tool for creating a Canvas with CanvasScaler, GraphicRaycaster, and optionally EventSystem
    /// </summary>
    public class CreateCanvasTool : McpToolBase
    {
        public CreateCanvasTool()
        {
            Name = "create_canvas";
            Description = "Creates a Canvas with CanvasScaler and GraphicRaycaster components, and optionally an EventSystem";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                // Extract parameters
                string objectPath = parameters["objectPath"]?.ToObject<string>();
                string renderModeStr = parameters["renderMode"]?.ToObject<string>() ?? "ScreenSpaceOverlay";
                string cameraPath = parameters["cameraPath"]?.ToObject<string>();
                int? sortingOrder = parameters["sortingOrder"]?.ToObject<int?>();
                bool pixelPerfect = parameters["pixelPerfect"]?.ToObject<bool?>() ?? false;
                bool createEventSystem = parameters["createEventSystem"]?.ToObject<bool?>() ?? true;
                JObject scalerParams = parameters["scaler"] as JObject;

                // Validate required parameters
                if (string.IsNullOrEmpty(objectPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Required parameter 'objectPath' not provided",
                        "validation_error"
                    );
                }

                // Parse render mode
                RenderMode renderMode;
                switch (renderModeStr)
                {
                    case "ScreenSpaceOverlay":
                        renderMode = RenderMode.ScreenSpaceOverlay;
                        break;
                    case "ScreenSpaceCamera":
                        renderMode = RenderMode.ScreenSpaceCamera;
                        break;
                    case "WorldSpace":
                        renderMode = RenderMode.WorldSpace;
                        break;
                    default:
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Invalid renderMode '{renderModeStr}'. Valid values: ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace",
                            "validation_error"
                        );
                }

                // For Camera/WorldSpace modes, validate camera
                Camera targetCamera = null;
                if (renderMode == RenderMode.ScreenSpaceCamera || renderMode == RenderMode.WorldSpace)
                {
                    if (!string.IsNullOrEmpty(cameraPath))
                    {
                        GameObject cameraObj = GameObject.Find(cameraPath);
                        if (cameraObj != null)
                            targetCamera = cameraObj.GetComponent<Camera>();
                    }

                    if (targetCamera == null)
                    {
                        targetCamera = Camera.main;
                    }

                    if (targetCamera == null && renderMode == RenderMode.ScreenSpaceCamera)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            "ScreenSpaceCamera mode requires a camera. No camera found at specified path or as Main Camera.",
                            "validation_error"
                        );
                    }
                }

                // Create the Canvas GameObject
                GameObject canvasGO = GameObjectHierarchyCreator.FindOrCreateHierarchicalGameObject(objectPath);

                // Check if Canvas already exists
                Canvas existingCanvas = canvasGO.GetComponent<Canvas>();
                if (existingCanvas != null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Canvas already exists at '{objectPath}'",
                        "validation_error"
                    );
                }

                // Add Canvas component
                Undo.RecordObject(canvasGO, "Create Canvas");
                Canvas canvas = Undo.AddComponent<Canvas>(canvasGO);
                canvas.renderMode = renderMode;
                canvas.pixelPerfect = pixelPerfect;

                if (renderMode == RenderMode.ScreenSpaceCamera || renderMode == RenderMode.WorldSpace)
                {
                    canvas.worldCamera = targetCamera;
                }

                if (sortingOrder.HasValue)
                {
                    canvas.sortingOrder = sortingOrder.Value;
                }

                // Add CanvasScaler
                CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(canvasGO);
                if (scalerParams != null)
                {
                    string scaleModeStr = scalerParams["uiScaleMode"]?.ToObject<string>();
                    if (!string.IsNullOrEmpty(scaleModeStr))
                    {
                        switch (scaleModeStr)
                        {
                            case "ConstantPixelSize":
                                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                                break;
                            case "ScaleWithScreenSize":
                                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                                break;
                            case "ConstantPhysicalSize":
                                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPhysicalSize;
                                break;
                        }
                    }

                    JObject refRes = scalerParams["referenceResolution"] as JObject;
                    if (refRes != null)
                    {
                        scaler.referenceResolution = UGUIToolUtils.ParseVector2(refRes, new Vector2(1920, 1080));
                    }

                    string screenMatchModeStr = scalerParams["screenMatchMode"]?.ToObject<string>();
                    if (!string.IsNullOrEmpty(screenMatchModeStr))
                    {
                        switch (screenMatchModeStr)
                        {
                            case "MatchWidthOrHeight":
                                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                                break;
                            case "Expand":
                                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                                break;
                            case "Shrink":
                                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                                break;
                        }
                    }

                    float? matchWidthOrHeight = scalerParams["matchWidthOrHeight"]?.ToObject<float?>();
                    if (matchWidthOrHeight.HasValue)
                    {
                        scaler.matchWidthOrHeight = Mathf.Clamp01(matchWidthOrHeight.Value);
                    }
                }

                // Add GraphicRaycaster
                Undo.AddComponent<GraphicRaycaster>(canvasGO);

                // Create EventSystem if requested
                if (createEventSystem)
                {
                    UGUIToolUtils.EnsureEventSystem();
                }

                EditorUtility.SetDirty(canvasGO);

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully created Canvas at '{objectPath}'",
                    ["instanceId"] = canvasGO.GetInstanceID(),
                    ["path"] = UGUIToolUtils.GetGameObjectPath(canvasGO)
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error creating Canvas: {ex.Message}",
                    "canvas_error"
                );
            }
        }
    }

    #endregion

    #region CreateUIElementTool

    /// <summary>
    /// Tool for creating UI elements (Button, Text, Image, Panel, etc.)
    /// </summary>
    public class CreateUIElementTool : McpToolBase
    {
        public CreateUIElementTool()
        {
            Name = "create_ui_element";
            Description = "Creates a UI element (Button, Text, TextMeshPro, Image, RawImage, Panel, InputField, Toggle, Slider, Dropdown, ScrollView, Scrollbar)";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                // Extract parameters
                string objectPath = parameters["objectPath"]?.ToObject<string>();
                string elementType = parameters["elementType"]?.ToObject<string>();
                JObject rectTransformParams = parameters["rectTransform"] as JObject;
                JObject elementData = parameters["elementData"] as JObject;

                // Validate required parameters
                if (string.IsNullOrEmpty(objectPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Required parameter 'objectPath' not provided",
                        "validation_error"
                    );
                }

                if (string.IsNullOrEmpty(elementType))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Required parameter 'elementType' not provided",
                        "validation_error"
                    );
                }

                // Create or find the GameObject
                GameObject elementGO = GameObjectHierarchyCreator.FindOrCreateHierarchicalGameObject(objectPath);

                // Ensure parent has a Canvas (required for UI elements)
                Canvas parentCanvas = elementGO.GetComponentInParent<Canvas>();
                if (parentCanvas == null)
                {
                    // Find or create a canvas as parent
                    string[] pathParts = objectPath.Split('/');
                    if (pathParts.Length > 1)
                    {
                        // Check if first part is a canvas
                        GameObject rootObj = GameObject.Find(pathParts[0]);
                        if (rootObj != null && rootObj.GetComponent<Canvas>() == null)
                        {
                            // Add Canvas to root
                            Undo.RecordObject(rootObj, "Add Canvas");
                            Canvas newCanvas = Undo.AddComponent<Canvas>(rootObj);
                            newCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                            Undo.AddComponent<CanvasScaler>(rootObj);
                            Undo.AddComponent<GraphicRaycaster>(rootObj);
                            UGUIToolUtils.EnsureEventSystem();
                            parentCanvas = newCanvas;
                        }
                        else if (rootObj != null)
                        {
                            parentCanvas = rootObj.GetComponent<Canvas>();
                        }
                    }

                    if (parentCanvas == null)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            "UI elements must be children of a Canvas. No Canvas found in parent hierarchy.",
                            "canvas_error"
                        );
                    }
                }

                // Ensure RectTransform exists
                RectTransform rectTransform = elementGO.GetComponent<RectTransform>();
                if (rectTransform == null)
                {
                    // RectTransform is automatically added when we add a UI component
                    // but we need to ensure the object has one for positioning
                }

                Undo.RecordObject(elementGO, $"Create UI Element {elementType}");

                // Create the UI element based on type
                bool usedFallback = false;
                string createdType = elementType;

                switch (elementType)
                {
                    case "Button":
                        CreateButton(elementGO, elementData);
                        break;

                    case "Text":
                        CreateText(elementGO, elementData);
                        break;

                    case "TextMeshPro":
                        if (UGUIToolUtils.IsTMProAvailable())
                        {
                            CreateTextMeshPro(elementGO, elementData);
                        }
                        else
                        {
                            CreateText(elementGO, elementData);
                            usedFallback = true;
                            createdType = "Text (TMPro not available)";
                        }
                        break;

                    case "Image":
                        CreateImage(elementGO, elementData);
                        break;

                    case "RawImage":
                        CreateRawImage(elementGO, elementData);
                        break;

                    case "Panel":
                        CreatePanel(elementGO, elementData);
                        break;

                    case "InputField":
                        CreateInputField(elementGO, elementData);
                        break;

                    case "InputFieldTMP":
                        if (UGUIToolUtils.IsTMProAvailable())
                        {
                            CreateInputFieldTMP(elementGO, elementData);
                        }
                        else
                        {
                            CreateInputField(elementGO, elementData);
                            usedFallback = true;
                            createdType = "InputField (TMPro not available)";
                        }
                        break;

                    case "Toggle":
                        CreateToggle(elementGO, elementData);
                        break;

                    case "Slider":
                        CreateSlider(elementGO, elementData);
                        break;

                    case "Dropdown":
                        CreateDropdown(elementGO, elementData);
                        break;

                    case "DropdownTMP":
                        if (UGUIToolUtils.IsTMProAvailable())
                        {
                            CreateDropdownTMP(elementGO, elementData);
                        }
                        else
                        {
                            CreateDropdown(elementGO, elementData);
                            usedFallback = true;
                            createdType = "Dropdown (TMPro not available)";
                        }
                        break;

                    case "ScrollView":
                        CreateScrollView(elementGO, elementData);
                        break;

                    case "Scrollbar":
                        CreateScrollbar(elementGO, elementData);
                        break;

                    default:
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Unknown element type '{elementType}'. Valid types: Button, Text, TextMeshPro, Image, RawImage, Panel, InputField, InputFieldTMP, Toggle, Slider, Dropdown, DropdownTMP, ScrollView, Scrollbar",
                            "validation_error"
                        );
                }

                // Apply RectTransform settings
                rectTransform = elementGO.GetComponent<RectTransform>();
                if (rectTransform != null && rectTransformParams != null)
                {
                    ApplyRectTransformParams(rectTransform, rectTransformParams);
                }

                EditorUtility.SetDirty(elementGO);

                string message = $"Successfully created {createdType} at '{objectPath}'";
                if (usedFallback)
                {
                    message += " (TextMeshPro package not installed, used legacy UI fallback)";
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = message,
                    ["instanceId"] = elementGO.GetInstanceID(),
                    ["path"] = UGUIToolUtils.GetGameObjectPath(elementGO),
                    ["usedFallback"] = usedFallback
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error creating UI element: {ex.Message}",
                    "component_error"
                );
            }
        }

        private void ApplyRectTransformParams(RectTransform rect, JObject rtParams)
        {
            Undo.RecordObject(rect, "Apply RectTransform Settings");

            string anchorPreset = rtParams["anchorPreset"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(anchorPreset))
            {
                UGUIToolUtils.ApplyAnchorPreset(rect, anchorPreset);
            }

            JObject anchorMin = rtParams["anchorMin"] as JObject;
            if (anchorMin != null)
            {
                rect.anchorMin = UGUIToolUtils.ParseVector2(anchorMin, rect.anchorMin);
            }

            JObject anchorMax = rtParams["anchorMax"] as JObject;
            if (anchorMax != null)
            {
                rect.anchorMax = UGUIToolUtils.ParseVector2(anchorMax, rect.anchorMax);
            }

            JObject pivot = rtParams["pivot"] as JObject;
            if (pivot != null)
            {
                rect.pivot = UGUIToolUtils.ParseVector2(pivot, rect.pivot);
            }

            JObject anchoredPosition = rtParams["anchoredPosition"] as JObject;
            if (anchoredPosition != null)
            {
                rect.anchoredPosition = UGUIToolUtils.ParseVector2(anchoredPosition, rect.anchoredPosition);
            }

            JObject sizeDelta = rtParams["sizeDelta"] as JObject;
            if (sizeDelta != null)
            {
                rect.sizeDelta = UGUIToolUtils.ParseVector2(sizeDelta, rect.sizeDelta);
            }
        }

        private void CreateButton(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = new Color(1, 1, 1, 1);
            }

            Button button = go.GetComponent<Button>();
            if (button == null)
            {
                button = Undo.AddComponent<Button>(go);
            }

            // Set interactable
            if (data != null)
            {
                bool? interactable = data["interactable"]?.ToObject<bool?>();
                if (interactable.HasValue)
                    button.interactable = interactable.Value;

                JObject colorObj = data["color"] as JObject;
                if (colorObj != null)
                    image.color = UGUIToolUtils.ParseColor(colorObj, image.color);
            }

            // Create child text
            string buttonText = data?["text"]?.ToObject<string>() ?? "Button";
            GameObject textGO = new GameObject("Text");
            Undo.RegisterCreatedObjectUndo(textGO, "Create Button Text");
            textGO.transform.SetParent(go.transform, false);

            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            Text text = textGO.AddComponent<Text>();
            text.text = buttonText;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (data != null)
            {
                int? fontSize = data["fontSize"]?.ToObject<int?>();
                if (fontSize.HasValue)
                    text.fontSize = fontSize.Value;
            }

            // Set default size for button
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 30);
            }
        }

        private void CreateText(GameObject go, JObject data)
        {
            Text text = go.GetComponent<Text>();
            if (text == null)
            {
                text = Undo.AddComponent<Text>(go);
                text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (data != null)
            {
                string textContent = data["text"]?.ToObject<string>();
                if (textContent != null)
                    text.text = textContent;

                int? fontSize = data["fontSize"]?.ToObject<int?>();
                if (fontSize.HasValue)
                    text.fontSize = fontSize.Value;

                JObject colorObj = data["color"] as JObject;
                if (colorObj != null)
                    text.color = UGUIToolUtils.ParseColor(colorObj, text.color);

                string alignment = data["alignment"]?.ToObject<string>();
                if (!string.IsNullOrEmpty(alignment) && Enum.TryParse<TextAnchor>(alignment, true, out var anchor))
                    text.alignment = anchor;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 30);
            }
        }

        private void CreateTextMeshPro(GameObject go, JObject data)
        {
            // Use reflection to create TMPro text to avoid compile-time dependency
            Type tmpTextType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpTextType == null)
            {
                // Fallback to legacy Text
                CreateText(go, data);
                return;
            }

            Component tmpText = go.GetComponent(tmpTextType);
            if (tmpText == null)
            {
                tmpText = Undo.AddComponent(go, tmpTextType);
            }

            if (data != null)
            {
                string textContent = data["text"]?.ToObject<string>();
                if (textContent != null)
                {
                    PropertyInfo textProp = tmpTextType.GetProperty("text");
                    textProp?.SetValue(tmpText, textContent);
                }

                float? fontSize = data["fontSize"]?.ToObject<float?>();
                if (fontSize.HasValue)
                {
                    PropertyInfo fontSizeProp = tmpTextType.GetProperty("fontSize");
                    fontSizeProp?.SetValue(tmpText, fontSize.Value);
                }

                JObject colorObj = data["color"] as JObject;
                if (colorObj != null)
                {
                    PropertyInfo colorProp = tmpTextType.GetProperty("color");
                    colorProp?.SetValue(tmpText, UGUIToolUtils.ParseColor(colorObj, Color.white));
                }
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(200, 50);
            }
        }

        private void CreateImage(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
            }

            if (data != null)
            {
                JObject colorObj = data["color"] as JObject;
                if (colorObj != null)
                    image.color = UGUIToolUtils.ParseColor(colorObj, image.color);

                bool? raycastTarget = data["raycastTarget"]?.ToObject<bool?>();
                if (raycastTarget.HasValue)
                    image.raycastTarget = raycastTarget.Value;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(100, 100);
            }
        }

        private void CreateRawImage(GameObject go, JObject data)
        {
            RawImage rawImage = go.GetComponent<RawImage>();
            if (rawImage == null)
            {
                rawImage = Undo.AddComponent<RawImage>(go);
            }

            if (data != null)
            {
                JObject colorObj = data["color"] as JObject;
                if (colorObj != null)
                    rawImage.color = UGUIToolUtils.ParseColor(colorObj, rawImage.color);

                bool? raycastTarget = data["raycastTarget"]?.ToObject<bool?>();
                if (raycastTarget.HasValue)
                    rawImage.raycastTarget = raycastTarget.Value;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(100, 100);
            }
        }

        private void CreatePanel(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = new Color(1, 1, 1, 0.39f); // Default panel color
            }

            if (data != null)
            {
                JObject colorObj = data["color"] as JObject;
                if (colorObj != null)
                    image.color = UGUIToolUtils.ParseColor(colorObj, image.color);
            }

            // Set default to stretch
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private void CreateInputField(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = Color.white;
            }

            InputField inputField = go.GetComponent<InputField>();
            if (inputField == null)
            {
                inputField = Undo.AddComponent<InputField>(go);
            }

            // Create text area
            GameObject textArea = new GameObject("Text Area");
            Undo.RegisterCreatedObjectUndo(textArea, "Create Input Field Text Area");
            textArea.transform.SetParent(go.transform, false);
            RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.sizeDelta = new Vector2(-20, -10);
            textAreaRect.anchoredPosition = Vector2.zero;
            RectMask2D mask = textArea.AddComponent<RectMask2D>();

            // Create placeholder
            GameObject placeholder = new GameObject("Placeholder");
            Undo.RegisterCreatedObjectUndo(placeholder, "Create Input Field Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            RectTransform placeholderRect = placeholder.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.sizeDelta = Vector2.zero;
            placeholderRect.anchoredPosition = Vector2.zero;
            Text placeholderText = placeholder.AddComponent<Text>();
            placeholderText.text = data?["placeholder"]?.ToObject<string>() ?? "Enter text...";
            placeholderText.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            placeholderText.alignment = TextAnchor.MiddleLeft;

            // Create text
            GameObject textGO = new GameObject("Text");
            Undo.RegisterCreatedObjectUndo(textGO, "Create Input Field Text");
            textGO.transform.SetParent(textArea.transform, false);
            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;
            Text text = textGO.AddComponent<Text>();
            text.text = "";
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            inputField.textComponent = text;
            inputField.placeholder = placeholderText;

            if (data != null)
            {
                string initialText = data["text"]?.ToObject<string>();
                if (initialText != null)
                    inputField.text = initialText;

                bool? interactable = data["interactable"]?.ToObject<bool?>();
                if (interactable.HasValue)
                    inputField.interactable = interactable.Value;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 30);
            }
        }

        private void CreateInputFieldTMP(GameObject go, JObject data)
        {
            Type tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
            if (tmpInputType == null)
            {
                CreateInputField(go, data);
                return;
            }

            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = Color.white;
            }

            Component tmpInput = go.GetComponent(tmpInputType);
            if (tmpInput == null)
            {
                tmpInput = Undo.AddComponent(go, tmpInputType);
            }

            // For TMP InputField, we need to create the viewport and text components
            // This is complex due to TMP's structure - simplified version
            // In production, you'd want to instantiate from a prefab

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 30);
            }
        }

        private void CreateToggle(GameObject go, JObject data)
        {
            Toggle toggle = go.GetComponent<Toggle>();
            if (toggle == null)
            {
                toggle = Undo.AddComponent<Toggle>(go);
            }

            // Create background
            GameObject background = new GameObject("Background");
            Undo.RegisterCreatedObjectUndo(background, "Create Toggle Background");
            background.transform.SetParent(go.transform, false);
            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.5f);
            bgRect.anchorMax = new Vector2(0, 0.5f);
            bgRect.pivot = new Vector2(0, 0.5f);
            bgRect.sizeDelta = new Vector2(20, 20);
            bgRect.anchoredPosition = Vector2.zero;
            Image bgImage = background.AddComponent<Image>();
            bgImage.color = Color.white;

            // Create checkmark
            GameObject checkmark = new GameObject("Checkmark");
            Undo.RegisterCreatedObjectUndo(checkmark, "Create Toggle Checkmark");
            checkmark.transform.SetParent(background.transform, false);
            RectTransform checkRect = checkmark.AddComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.1f, 0.1f);
            checkRect.anchorMax = new Vector2(0.9f, 0.9f);
            checkRect.sizeDelta = Vector2.zero;
            checkRect.anchoredPosition = Vector2.zero;
            Image checkImage = checkmark.AddComponent<Image>();
            checkImage.color = new Color(0.2f, 0.2f, 0.2f, 1);

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;

            // Create label
            GameObject label = new GameObject("Label");
            Undo.RegisterCreatedObjectUndo(label, "Create Toggle Label");
            label.transform.SetParent(go.transform, false);
            RectTransform labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(25, 0);
            labelRect.offsetMax = Vector2.zero;
            Text labelText = label.AddComponent<Text>();
            labelText.text = data?["text"]?.ToObject<string>() ?? "Toggle";
            labelText.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.color = Color.black;
            labelText.alignment = TextAnchor.MiddleLeft;

            if (data != null)
            {
                bool? isOn = data["isOn"]?.ToObject<bool?>();
                if (isOn.HasValue)
                    toggle.isOn = isOn.Value;

                bool? interactable = data["interactable"]?.ToObject<bool?>();
                if (interactable.HasValue)
                    toggle.interactable = interactable.Value;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 20);
            }
        }

        private void CreateSlider(GameObject go, JObject data)
        {
            Slider slider = go.GetComponent<Slider>();
            if (slider == null)
            {
                slider = Undo.AddComponent<Slider>(go);
            }

            // Create background
            GameObject background = new GameObject("Background");
            Undo.RegisterCreatedObjectUndo(background, "Create Slider Background");
            background.transform.SetParent(go.transform, false);
            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            bgRect.anchoredPosition = Vector2.zero;
            Image bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(0.8f, 0.8f, 0.8f, 1);

            // Create fill area
            GameObject fillArea = new GameObject("Fill Area");
            Undo.RegisterCreatedObjectUndo(fillArea, "Create Slider Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-15, 0);

            // Create fill
            GameObject fill = new GameObject("Fill");
            Undo.RegisterCreatedObjectUndo(fill, "Create Slider Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.sizeDelta = new Vector2(10, 0);
            fillRect.anchoredPosition = Vector2.zero;
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.3f, 0.3f, 1);

            // Create handle slide area
            GameObject handleArea = new GameObject("Handle Slide Area");
            Undo.RegisterCreatedObjectUndo(handleArea, "Create Slider Handle Area");
            handleArea.transform.SetParent(go.transform, false);
            RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0, 0);
            handleAreaRect.anchorMax = new Vector2(1, 1);
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            // Create handle
            GameObject handle = new GameObject("Handle");
            Undo.RegisterCreatedObjectUndo(handle, "Create Slider Handle");
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.sizeDelta = new Vector2(20, 0);
            handleRect.anchoredPosition = Vector2.zero;
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;

            if (data != null)
            {
                float? value = data["value"]?.ToObject<float?>();
                if (value.HasValue)
                    slider.value = value.Value;

                float? minValue = data["minValue"]?.ToObject<float?>();
                if (minValue.HasValue)
                    slider.minValue = minValue.Value;

                float? maxValue = data["maxValue"]?.ToObject<float?>();
                if (maxValue.HasValue)
                    slider.maxValue = maxValue.Value;

                bool? wholeNumbers = data["wholeNumbers"]?.ToObject<bool?>();
                if (wholeNumbers.HasValue)
                    slider.wholeNumbers = wholeNumbers.Value;

                bool? interactable = data["interactable"]?.ToObject<bool?>();
                if (interactable.HasValue)
                    slider.interactable = interactable.Value;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 20);
            }
        }

        private void CreateDropdown(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = Color.white;
            }

            Dropdown dropdown = go.GetComponent<Dropdown>();
            if (dropdown == null)
            {
                dropdown = Undo.AddComponent<Dropdown>(go);
            }

            // Create label
            GameObject label = new GameObject("Label");
            Undo.RegisterCreatedObjectUndo(label, "Create Dropdown Label");
            label.transform.SetParent(go.transform, false);
            RectTransform labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-25, 0);
            Text labelText = label.AddComponent<Text>();
            labelText.text = "Option A";
            labelText.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.color = Color.black;
            labelText.alignment = TextAnchor.MiddleLeft;

            // Create arrow
            GameObject arrow = new GameObject("Arrow");
            Undo.RegisterCreatedObjectUndo(arrow, "Create Dropdown Arrow");
            arrow.transform.SetParent(go.transform, false);
            RectTransform arrowRect = arrow.AddComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0.5f);
            arrowRect.anchorMax = new Vector2(1, 0.5f);
            arrowRect.pivot = new Vector2(1, 0.5f);
            arrowRect.sizeDelta = new Vector2(20, 20);
            arrowRect.anchoredPosition = new Vector2(-5, 0);
            Image arrowImage = arrow.AddComponent<Image>();
            arrowImage.color = Color.black;

            dropdown.targetGraphic = image;
            dropdown.captionText = labelText;

            // Add default options
            dropdown.options.Clear();
            dropdown.options.Add(new Dropdown.OptionData("Option A"));
            dropdown.options.Add(new Dropdown.OptionData("Option B"));
            dropdown.options.Add(new Dropdown.OptionData("Option C"));

            if (data != null)
            {
                JArray options = data["options"] as JArray;
                if (options != null && options.Count > 0)
                {
                    dropdown.options.Clear();
                    foreach (var opt in options)
                    {
                        dropdown.options.Add(new Dropdown.OptionData(opt.ToObject<string>()));
                    }
                }

                int? value = data["value"]?.ToObject<int?>();
                if (value.HasValue)
                    dropdown.value = value.Value;

                bool? interactable = data["interactable"]?.ToObject<bool?>();
                if (interactable.HasValue)
                    dropdown.interactable = interactable.Value;
            }

            // Create Template structure for runtime functionality
            CreateDropdownTemplate(go, dropdown, labelText.font);

            dropdown.RefreshShownValue();

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 30);
            }
        }

        /// <summary>
        /// Creates the Template structure required for Dropdown to function at runtime
        /// </summary>
        private void CreateDropdownTemplate(GameObject dropdownGO, Dropdown dropdown, Font font)
        {
            // 1. Template Object (hidden by default)
            GameObject template = new GameObject("Template");
            Undo.RegisterCreatedObjectUndo(template, "Create Dropdown Template");
            template.transform.SetParent(dropdownGO.transform, false);
            template.SetActive(false);

            RectTransform templateRect = template.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.anchoredPosition = new Vector2(0, 2);
            templateRect.sizeDelta = new Vector2(0, 150);

            Image templateImage = template.AddComponent<Image>();
            templateImage.color = Color.white;

            ScrollRect scrollRect = template.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.verticalScrollbarSpacing = -3;

            // 2. Viewport
            GameObject viewport = new GameObject("Viewport");
            Undo.RegisterCreatedObjectUndo(viewport, "Create Dropdown Viewport");
            viewport.transform.SetParent(template.transform, false);

            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = new Vector2(-18, 0);
            viewportRect.pivot = new Vector2(0, 1);
            viewportRect.anchoredPosition = Vector2.zero;

            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = Color.white;
            viewportImage.type = Image.Type.Sliced;
            Mask viewportMask = viewport.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            // 3. Content
            GameObject content = new GameObject("Content");
            Undo.RegisterCreatedObjectUndo(content, "Create Dropdown Content");
            content.transform.SetParent(viewport.transform, false);

            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 28);
            contentRect.anchoredPosition = Vector2.zero;

            // 4. Item (template for each option)
            GameObject item = new GameObject("Item");
            Undo.RegisterCreatedObjectUndo(item, "Create Dropdown Item");
            item.transform.SetParent(content.transform, false);

            RectTransform itemRect = item.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 20);
            itemRect.anchoredPosition = Vector2.zero;

            Toggle itemToggle = item.AddComponent<Toggle>();

            // Item Background
            GameObject itemBg = new GameObject("Item Background");
            Undo.RegisterCreatedObjectUndo(itemBg, "Create Item Background");
            itemBg.transform.SetParent(item.transform, false);

            RectTransform itemBgRect = itemBg.AddComponent<RectTransform>();
            itemBgRect.anchorMin = Vector2.zero;
            itemBgRect.anchorMax = Vector2.one;
            itemBgRect.sizeDelta = Vector2.zero;
            itemBgRect.anchoredPosition = Vector2.zero;

            Image itemBgImage = itemBg.AddComponent<Image>();
            itemBgImage.color = new Color(0.96f, 0.96f, 0.96f, 1f);

            // Item Checkmark
            GameObject itemCheck = new GameObject("Item Checkmark");
            Undo.RegisterCreatedObjectUndo(itemCheck, "Create Item Checkmark");
            itemCheck.transform.SetParent(item.transform, false);

            RectTransform itemCheckRect = itemCheck.AddComponent<RectTransform>();
            itemCheckRect.anchorMin = new Vector2(0, 0.5f);
            itemCheckRect.anchorMax = new Vector2(0, 0.5f);
            itemCheckRect.sizeDelta = new Vector2(20, 20);
            itemCheckRect.anchoredPosition = new Vector2(10, 0);

            Image itemCheckImage = itemCheck.AddComponent<Image>();
            itemCheckImage.color = Color.black;

            // Item Label
            GameObject itemLabel = new GameObject("Item Label");
            Undo.RegisterCreatedObjectUndo(itemLabel, "Create Item Label");
            itemLabel.transform.SetParent(item.transform, false);

            RectTransform itemLabelRect = itemLabel.AddComponent<RectTransform>();
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(20, 0);
            itemLabelRect.offsetMax = Vector2.zero;

            Text itemLabelText = itemLabel.AddComponent<Text>();
            itemLabelText.font = font;
            itemLabelText.color = Color.black;
            itemLabelText.alignment = TextAnchor.MiddleLeft;

            // Configure Item Toggle
            itemToggle.targetGraphic = itemBgImage;
            itemToggle.graphic = itemCheckImage;
            itemToggle.isOn = true;

            // 5. Scrollbar
            GameObject scrollbar = new GameObject("Scrollbar");
            Undo.RegisterCreatedObjectUndo(scrollbar, "Create Dropdown Scrollbar");
            scrollbar.transform.SetParent(template.transform, false);

            RectTransform scrollbarRect = scrollbar.AddComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1, 0);
            scrollbarRect.anchorMax = new Vector2(1, 1);
            scrollbarRect.pivot = new Vector2(1, 1);
            scrollbarRect.sizeDelta = new Vector2(20, 0);
            scrollbarRect.anchoredPosition = Vector2.zero;

            Image scrollbarImage = scrollbar.AddComponent<Image>();
            scrollbarImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);

            Scrollbar scrollbarComp = scrollbar.AddComponent<Scrollbar>();
            scrollbarComp.direction = Scrollbar.Direction.BottomToTop;

            // Scrollbar Sliding Area
            GameObject slidingArea = new GameObject("Sliding Area");
            Undo.RegisterCreatedObjectUndo(slidingArea, "Create Sliding Area");
            slidingArea.transform.SetParent(scrollbar.transform, false);

            RectTransform slidingRect = slidingArea.AddComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(10, 10);
            slidingRect.offsetMax = new Vector2(-10, -10);

            // Scrollbar Handle
            GameObject handle = new GameObject("Handle");
            Undo.RegisterCreatedObjectUndo(handle, "Create Handle");
            handle.transform.SetParent(slidingArea.transform, false);

            RectTransform handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.sizeDelta = Vector2.zero;
            handleRect.anchoredPosition = Vector2.zero;

            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.5f, 0.5f, 0.5f, 1f);

            scrollbarComp.handleRect = handleRect;
            scrollbarComp.targetGraphic = handleImage;

            // Link everything to Dropdown
            dropdown.template = templateRect;
            dropdown.itemText = itemLabelText;

            // Link ScrollRect references
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.verticalScrollbar = scrollbarComp;
        }

        private void CreateDropdownTMP(GameObject go, JObject data)
        {
            Type tmpDropdownType = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
            if (tmpDropdownType == null)
            {
                CreateDropdown(go, data);
                return;
            }

            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = Color.white;
            }

            Component tmpDropdown = go.GetComponent(tmpDropdownType);
            if (tmpDropdown == null)
            {
                tmpDropdown = Undo.AddComponent(go, tmpDropdownType);
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(160, 30);
            }
        }

        private void CreateScrollView(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            }

            ScrollRect scrollRect = go.GetComponent<ScrollRect>();
            if (scrollRect == null)
            {
                scrollRect = Undo.AddComponent<ScrollRect>(go);
            }

            // Create Viewport
            GameObject viewport = new GameObject("Viewport");
            Undo.RegisterCreatedObjectUndo(viewport, "Create ScrollView Viewport");
            viewport.transform.SetParent(go.transform, false);
            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.anchoredPosition = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Create Content
            GameObject content = new GameObject("Content");
            Undo.RegisterCreatedObjectUndo(content, "Create ScrollView Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.sizeDelta = new Vector2(0, 300);
            contentRect.anchoredPosition = Vector2.zero;

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;

            if (data != null)
            {
                bool? horizontal = data["horizontal"]?.ToObject<bool?>();
                if (horizontal.HasValue)
                    scrollRect.horizontal = horizontal.Value;

                bool? vertical = data["vertical"]?.ToObject<bool?>();
                if (vertical.HasValue)
                    scrollRect.vertical = vertical.Value;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(200, 200);
            }
        }

        private void CreateScrollbar(GameObject go, JObject data)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(go);
                image.color = new Color(0.8f, 0.8f, 0.8f, 1);
            }

            Scrollbar scrollbar = go.GetComponent<Scrollbar>();
            if (scrollbar == null)
            {
                scrollbar = Undo.AddComponent<Scrollbar>(go);
            }

            // Create sliding area
            GameObject slidingArea = new GameObject("Sliding Area");
            Undo.RegisterCreatedObjectUndo(slidingArea, "Create Scrollbar Sliding Area");
            slidingArea.transform.SetParent(go.transform, false);
            RectTransform slidingRect = slidingArea.AddComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(10, 10);
            slidingRect.offsetMax = new Vector2(-10, -10);

            // Create handle
            GameObject handle = new GameObject("Handle");
            Undo.RegisterCreatedObjectUndo(handle, "Create Scrollbar Handle");
            handle.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.sizeDelta = Vector2.zero;
            handleRect.anchoredPosition = Vector2.zero;
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.4f, 0.4f, 0.4f, 1);

            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRect;

            if (data != null)
            {
                float? value = data["value"]?.ToObject<float?>();
                if (value.HasValue)
                    scrollbar.value = value.Value;

                float? size = data["size"]?.ToObject<float?>();
                if (size.HasValue)
                    scrollbar.size = size.Value;

                string direction = data["direction"]?.ToObject<string>();
                if (!string.IsNullOrEmpty(direction) && Enum.TryParse<Scrollbar.Direction>(direction, true, out var dir))
                    scrollbar.direction = dir;
            }

            // Set default size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null && rect.sizeDelta == Vector2.zero)
            {
                rect.sizeDelta = new Vector2(20, 160);
            }
        }
    }

    #endregion

    #region SetRectTransformTool

    /// <summary>
    /// Tool for modifying RectTransform properties
    /// </summary>
    public class SetRectTransformTool : McpToolBase
    {
        public SetRectTransformTool()
        {
            Name = "set_rect_transform";
            Description = "Modifies RectTransform properties of a UI element (anchors, pivot, position, size, rotation, scale)";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                // Extract parameters
                int? instanceId = parameters["instanceId"]?.ToObject<int?>();
                string objectPath = parameters["objectPath"]?.ToObject<string>();

                // Validate parameters
                if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Either 'instanceId' or 'objectPath' must be provided",
                        "validation_error"
                    );
                }

                // Find the GameObject
                GameObject gameObject = UGUIToolUtils.FindGameObject(instanceId, objectPath, out string identifier);

                if (gameObject == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"GameObject not found using {identifier}",
                        "not_found_error"
                    );
                }

                // Get RectTransform
                RectTransform rect = gameObject.GetComponent<RectTransform>();
                if (rect == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"GameObject '{gameObject.name}' does not have a RectTransform component",
                        "component_error"
                    );
                }

                Undo.RecordObject(rect, "Set RectTransform");

                // Apply anchor preset
                string anchorPreset = parameters["anchorPreset"]?.ToObject<string>();
                if (!string.IsNullOrEmpty(anchorPreset))
                {
                    if (!UGUIToolUtils.AnchorPresets.ContainsKey(anchorPreset))
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Unknown anchor preset '{anchorPreset}'. Valid presets: {string.Join(", ", UGUIToolUtils.AnchorPresets.Keys)}",
                            "validation_error"
                        );
                    }
                    UGUIToolUtils.ApplyAnchorPreset(rect, anchorPreset);
                }

                // Apply individual anchor values
                JObject anchorMin = parameters["anchorMin"] as JObject;
                if (anchorMin != null)
                {
                    rect.anchorMin = UGUIToolUtils.ParseVector2(anchorMin, rect.anchorMin);
                }

                JObject anchorMax = parameters["anchorMax"] as JObject;
                if (anchorMax != null)
                {
                    rect.anchorMax = UGUIToolUtils.ParseVector2(anchorMax, rect.anchorMax);
                }

                JObject pivot = parameters["pivot"] as JObject;
                if (pivot != null)
                {
                    rect.pivot = UGUIToolUtils.ParseVector2(pivot, rect.pivot);
                }

                JObject anchoredPosition = parameters["anchoredPosition"] as JObject;
                if (anchoredPosition != null)
                {
                    rect.anchoredPosition = UGUIToolUtils.ParseVector2(anchoredPosition, rect.anchoredPosition);
                }

                JObject sizeDelta = parameters["sizeDelta"] as JObject;
                if (sizeDelta != null)
                {
                    rect.sizeDelta = UGUIToolUtils.ParseVector2(sizeDelta, rect.sizeDelta);
                }

                JObject offsetMin = parameters["offsetMin"] as JObject;
                if (offsetMin != null)
                {
                    rect.offsetMin = UGUIToolUtils.ParseVector2(offsetMin, rect.offsetMin);
                }

                JObject offsetMax = parameters["offsetMax"] as JObject;
                if (offsetMax != null)
                {
                    rect.offsetMax = UGUIToolUtils.ParseVector2(offsetMax, rect.offsetMax);
                }

                JObject rotation = parameters["rotation"] as JObject;
                if (rotation != null)
                {
                    rect.localEulerAngles = UGUIToolUtils.ParseVector3(rotation, rect.localEulerAngles);
                }

                JObject localScale = parameters["localScale"] as JObject;
                if (localScale != null)
                {
                    rect.localScale = UGUIToolUtils.ParseVector3(localScale, rect.localScale);
                }

                EditorUtility.SetDirty(gameObject);

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully updated RectTransform on '{gameObject.name}'",
                    ["instanceId"] = gameObject.GetInstanceID(),
                    ["path"] = UGUIToolUtils.GetGameObjectPath(gameObject),
                    ["rectTransform"] = UGUIToolUtils.GetRectTransformInfo(rect)
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error setting RectTransform: {ex.Message}",
                    "component_error"
                );
            }
        }
    }

    #endregion

    #region AddLayoutComponentTool

    /// <summary>
    /// Tool for adding layout components to UI elements
    /// </summary>
    public class AddLayoutComponentTool : McpToolBase
    {
        public AddLayoutComponentTool()
        {
            Name = "add_layout_component";
            Description = "Adds a layout component (HorizontalLayoutGroup, VerticalLayoutGroup, GridLayoutGroup, ContentSizeFitter, LayoutElement, AspectRatioFitter) to a UI element";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                // Extract parameters
                int? instanceId = parameters["instanceId"]?.ToObject<int?>();
                string objectPath = parameters["objectPath"]?.ToObject<string>();
                string layoutType = parameters["layoutType"]?.ToObject<string>();
                JObject layoutData = parameters["layoutData"] as JObject;

                // Validate parameters
                if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Either 'instanceId' or 'objectPath' must be provided",
                        "validation_error"
                    );
                }

                if (string.IsNullOrEmpty(layoutType))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Required parameter 'layoutType' not provided",
                        "validation_error"
                    );
                }

                // Find the GameObject
                GameObject gameObject = UGUIToolUtils.FindGameObject(instanceId, objectPath, out string identifier);

                if (gameObject == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"GameObject not found using {identifier}",
                        "not_found_error"
                    );
                }

                Undo.RecordObject(gameObject, $"Add {layoutType}");

                Component addedComponent = null;
                string componentName = layoutType;

                switch (layoutType)
                {
                    case "HorizontalLayoutGroup":
                        addedComponent = AddHorizontalLayoutGroup(gameObject, layoutData);
                        break;

                    case "VerticalLayoutGroup":
                        addedComponent = AddVerticalLayoutGroup(gameObject, layoutData);
                        break;

                    case "GridLayoutGroup":
                        addedComponent = AddGridLayoutGroup(gameObject, layoutData);
                        break;

                    case "ContentSizeFitter":
                        addedComponent = AddContentSizeFitter(gameObject, layoutData);
                        break;

                    case "LayoutElement":
                        addedComponent = AddLayoutElement(gameObject, layoutData);
                        break;

                    case "AspectRatioFitter":
                        addedComponent = AddAspectRatioFitter(gameObject, layoutData);
                        break;

                    default:
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Unknown layout type '{layoutType}'. Valid types: HorizontalLayoutGroup, VerticalLayoutGroup, GridLayoutGroup, ContentSizeFitter, LayoutElement, AspectRatioFitter",
                            "validation_error"
                        );
                }

                EditorUtility.SetDirty(gameObject);

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully added {componentName} to '{gameObject.name}'",
                    ["instanceId"] = gameObject.GetInstanceID(),
                    ["path"] = UGUIToolUtils.GetGameObjectPath(gameObject)
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error adding layout component: {ex.Message}",
                    "component_error"
                );
            }
        }

        private HorizontalLayoutGroup AddHorizontalLayoutGroup(GameObject go, JObject data)
        {
            HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                layout = Undo.AddComponent<HorizontalLayoutGroup>(go);
            }

            ApplyLayoutGroupSettings(layout, data);
            return layout;
        }

        private VerticalLayoutGroup AddVerticalLayoutGroup(GameObject go, JObject data)
        {
            VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = Undo.AddComponent<VerticalLayoutGroup>(go);
            }

            ApplyLayoutGroupSettings(layout, data);
            return layout;
        }

        private void ApplyLayoutGroupSettings(HorizontalOrVerticalLayoutGroup layout, JObject data)
        {
            if (data == null) return;

            JObject padding = data["padding"] as JObject;
            if (padding != null)
            {
                layout.padding = UGUIToolUtils.ParseRectOffset(padding);
            }

            float? spacing = data["spacing"]?.ToObject<float?>();
            if (spacing.HasValue)
                layout.spacing = spacing.Value;

            string childAlignment = data["childAlignment"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(childAlignment) && Enum.TryParse<TextAnchor>(childAlignment, true, out var anchor))
                layout.childAlignment = anchor;

            bool? reverseArrangement = data["reverseArrangement"]?.ToObject<bool?>();
            if (reverseArrangement.HasValue)
                layout.reverseArrangement = reverseArrangement.Value;

            bool? childControlWidth = data["childControlWidth"]?.ToObject<bool?>();
            if (childControlWidth.HasValue)
                layout.childControlWidth = childControlWidth.Value;

            bool? childControlHeight = data["childControlHeight"]?.ToObject<bool?>();
            if (childControlHeight.HasValue)
                layout.childControlHeight = childControlHeight.Value;

            bool? childScaleWidth = data["childScaleWidth"]?.ToObject<bool?>();
            if (childScaleWidth.HasValue)
                layout.childScaleWidth = childScaleWidth.Value;

            bool? childScaleHeight = data["childScaleHeight"]?.ToObject<bool?>();
            if (childScaleHeight.HasValue)
                layout.childScaleHeight = childScaleHeight.Value;

            bool? childForceExpandWidth = data["childForceExpandWidth"]?.ToObject<bool?>();
            if (childForceExpandWidth.HasValue)
                layout.childForceExpandWidth = childForceExpandWidth.Value;

            bool? childForceExpandHeight = data["childForceExpandHeight"]?.ToObject<bool?>();
            if (childForceExpandHeight.HasValue)
                layout.childForceExpandHeight = childForceExpandHeight.Value;
        }

        private GridLayoutGroup AddGridLayoutGroup(GameObject go, JObject data)
        {
            GridLayoutGroup layout = go.GetComponent<GridLayoutGroup>();
            if (layout == null)
            {
                layout = Undo.AddComponent<GridLayoutGroup>(go);
            }

            if (data == null) return layout;

            JObject padding = data["padding"] as JObject;
            if (padding != null)
            {
                layout.padding = UGUIToolUtils.ParseRectOffset(padding);
            }

            JObject cellSize = data["cellSize"] as JObject;
            if (cellSize != null)
            {
                layout.cellSize = UGUIToolUtils.ParseVector2(cellSize, layout.cellSize);
            }

            JObject spacing = data["spacing"] as JObject;
            if (spacing != null)
            {
                layout.spacing = UGUIToolUtils.ParseVector2(spacing, layout.spacing);
            }

            string startCorner = data["startCorner"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(startCorner) && Enum.TryParse<GridLayoutGroup.Corner>(startCorner, true, out var corner))
                layout.startCorner = corner;

            string startAxis = data["startAxis"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(startAxis) && Enum.TryParse<GridLayoutGroup.Axis>(startAxis, true, out var axis))
                layout.startAxis = axis;

            string childAlignment = data["childAlignment"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(childAlignment) && Enum.TryParse<TextAnchor>(childAlignment, true, out var anchor))
                layout.childAlignment = anchor;

            string constraint = data["constraint"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(constraint))
            {
                switch (constraint)
                {
                    case "Flexible":
                        layout.constraint = GridLayoutGroup.Constraint.Flexible;
                        break;
                    case "FixedColumnCount":
                        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                        break;
                    case "FixedRowCount":
                        layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                        break;
                }
            }

            int? constraintCount = data["constraintCount"]?.ToObject<int?>();
            if (constraintCount.HasValue)
                layout.constraintCount = constraintCount.Value;

            return layout;
        }

        private ContentSizeFitter AddContentSizeFitter(GameObject go, JObject data)
        {
            ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = Undo.AddComponent<ContentSizeFitter>(go);
            }

            if (data == null) return fitter;

            string horizontalFit = data["horizontalFit"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(horizontalFit))
            {
                switch (horizontalFit)
                {
                    case "Unconstrained":
                        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                        break;
                    case "MinSize":
                        fitter.horizontalFit = ContentSizeFitter.FitMode.MinSize;
                        break;
                    case "PreferredSize":
                        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                        break;
                }
            }

            string verticalFit = data["verticalFit"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(verticalFit))
            {
                switch (verticalFit)
                {
                    case "Unconstrained":
                        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                        break;
                    case "MinSize":
                        fitter.verticalFit = ContentSizeFitter.FitMode.MinSize;
                        break;
                    case "PreferredSize":
                        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                        break;
                }
            }

            return fitter;
        }

        private LayoutElement AddLayoutElement(GameObject go, JObject data)
        {
            LayoutElement element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = Undo.AddComponent<LayoutElement>(go);
            }

            if (data == null) return element;

            bool? ignoreLayout = data["ignoreLayout"]?.ToObject<bool?>();
            if (ignoreLayout.HasValue)
                element.ignoreLayout = ignoreLayout.Value;

            float? minWidth = data["minWidth"]?.ToObject<float?>();
            if (minWidth.HasValue)
                element.minWidth = minWidth.Value;

            float? minHeight = data["minHeight"]?.ToObject<float?>();
            if (minHeight.HasValue)
                element.minHeight = minHeight.Value;

            float? preferredWidth = data["preferredWidth"]?.ToObject<float?>();
            if (preferredWidth.HasValue)
                element.preferredWidth = preferredWidth.Value;

            float? preferredHeight = data["preferredHeight"]?.ToObject<float?>();
            if (preferredHeight.HasValue)
                element.preferredHeight = preferredHeight.Value;

            float? flexibleWidth = data["flexibleWidth"]?.ToObject<float?>();
            if (flexibleWidth.HasValue)
                element.flexibleWidth = flexibleWidth.Value;

            float? flexibleHeight = data["flexibleHeight"]?.ToObject<float?>();
            if (flexibleHeight.HasValue)
                element.flexibleHeight = flexibleHeight.Value;

            int? layoutPriority = data["layoutPriority"]?.ToObject<int?>();
            if (layoutPriority.HasValue)
                element.layoutPriority = layoutPriority.Value;

            return element;
        }

        private AspectRatioFitter AddAspectRatioFitter(GameObject go, JObject data)
        {
            AspectRatioFitter fitter = go.GetComponent<AspectRatioFitter>();
            if (fitter == null)
            {
                fitter = Undo.AddComponent<AspectRatioFitter>(go);
            }

            if (data == null) return fitter;

            string aspectMode = data["aspectMode"]?.ToObject<string>();
            if (!string.IsNullOrEmpty(aspectMode) && Enum.TryParse<AspectRatioFitter.AspectMode>(aspectMode, true, out var mode))
                fitter.aspectMode = mode;

            float? aspectRatio = data["aspectRatio"]?.ToObject<float?>();
            if (aspectRatio.HasValue)
                fitter.aspectRatio = aspectRatio.Value;

            return fitter;
        }
    }

    #endregion

    #region GetUIElementInfoTool

    /// <summary>
    /// Tool for getting detailed information about a UI element
    /// </summary>
    public class GetUIElementInfoTool : McpToolBase
    {
        public GetUIElementInfoTool()
        {
            Name = "get_ui_element_info";
            Description = "Gets detailed information about a UI element including RectTransform, UI components, and layout settings";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                // Extract parameters
                int? instanceId = parameters["instanceId"]?.ToObject<int?>();
                string objectPath = parameters["objectPath"]?.ToObject<string>();
                bool includeChildren = parameters["includeChildren"]?.ToObject<bool?>() ?? false;

                // Validate parameters
                if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Either 'instanceId' or 'objectPath' must be provided",
                        "validation_error"
                    );
                }

                // Find the GameObject
                GameObject gameObject = UGUIToolUtils.FindGameObject(instanceId, objectPath, out string identifier);

                if (gameObject == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"GameObject not found using {identifier}",
                        "not_found_error"
                    );
                }

                // Build the info response
                JObject info = GetElementInfo(gameObject, includeChildren);

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Retrieved UI element info for '{gameObject.name}'",
                    ["elementInfo"] = info
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error getting UI element info: {ex.Message}",
                    "component_error"
                );
            }
        }

        private JObject GetElementInfo(GameObject go, bool includeChildren)
        {
            JObject info = new JObject
            {
                ["name"] = go.name,
                ["instanceId"] = go.GetInstanceID(),
                ["path"] = UGUIToolUtils.GetGameObjectPath(go),
                ["activeSelf"] = go.activeSelf,
                ["activeInHierarchy"] = go.activeInHierarchy
            };

            // RectTransform info
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                info["rectTransform"] = UGUIToolUtils.GetRectTransformInfo(rect);
            }

            // UI Components
            JArray components = new JArray();

            // Canvas
            Canvas canvas = go.GetComponent<Canvas>();
            if (canvas != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "Canvas",
                    ["renderMode"] = canvas.renderMode.ToString(),
                    ["sortingOrder"] = canvas.sortingOrder,
                    ["pixelPerfect"] = canvas.pixelPerfect
                });
            }

            // CanvasScaler
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "CanvasScaler",
                    ["uiScaleMode"] = scaler.uiScaleMode.ToString(),
                    ["referenceResolution"] = new JObject { ["x"] = scaler.referenceResolution.x, ["y"] = scaler.referenceResolution.y },
                    ["screenMatchMode"] = scaler.screenMatchMode.ToString(),
                    ["matchWidthOrHeight"] = scaler.matchWidthOrHeight
                });
            }

            // Image
            Image image = go.GetComponent<Image>();
            if (image != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "Image",
                    ["color"] = new JObject { ["r"] = image.color.r, ["g"] = image.color.g, ["b"] = image.color.b, ["a"] = image.color.a },
                    ["raycastTarget"] = image.raycastTarget,
                    ["hasSprite"] = image.sprite != null
                });
            }

            // RawImage
            RawImage rawImage = go.GetComponent<RawImage>();
            if (rawImage != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "RawImage",
                    ["color"] = new JObject { ["r"] = rawImage.color.r, ["g"] = rawImage.color.g, ["b"] = rawImage.color.b, ["a"] = rawImage.color.a },
                    ["raycastTarget"] = rawImage.raycastTarget,
                    ["hasTexture"] = rawImage.texture != null
                });
            }

            // Text
            Text text = go.GetComponent<Text>();
            if (text != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "Text",
                    ["text"] = text.text,
                    ["fontSize"] = text.fontSize,
                    ["alignment"] = text.alignment.ToString(),
                    ["color"] = new JObject { ["r"] = text.color.r, ["g"] = text.color.g, ["b"] = text.color.b, ["a"] = text.color.a }
                });
            }

            // Button
            Button button = go.GetComponent<Button>();
            if (button != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "Button",
                    ["interactable"] = button.interactable
                });
            }

            // Toggle
            Toggle toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "Toggle",
                    ["isOn"] = toggle.isOn,
                    ["interactable"] = toggle.interactable
                });
            }

            // Slider
            Slider slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "Slider",
                    ["value"] = slider.value,
                    ["minValue"] = slider.minValue,
                    ["maxValue"] = slider.maxValue,
                    ["wholeNumbers"] = slider.wholeNumbers,
                    ["interactable"] = slider.interactable
                });
            }

            // InputField
            InputField inputField = go.GetComponent<InputField>();
            if (inputField != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "InputField",
                    ["text"] = inputField.text,
                    ["interactable"] = inputField.interactable
                });
            }

            // Dropdown
            Dropdown dropdown = go.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                JArray options = new JArray();
                foreach (var opt in dropdown.options)
                {
                    options.Add(opt.text);
                }
                components.Add(new JObject
                {
                    ["type"] = "Dropdown",
                    ["value"] = dropdown.value,
                    ["options"] = options,
                    ["interactable"] = dropdown.interactable
                });
            }

            // ScrollRect
            ScrollRect scrollRect = go.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "ScrollRect",
                    ["horizontal"] = scrollRect.horizontal,
                    ["vertical"] = scrollRect.vertical
                });
            }

            // Layout components
            HorizontalLayoutGroup hLayout = go.GetComponent<HorizontalLayoutGroup>();
            if (hLayout != null)
            {
                components.Add(GetLayoutGroupInfo("HorizontalLayoutGroup", hLayout));
            }

            VerticalLayoutGroup vLayout = go.GetComponent<VerticalLayoutGroup>();
            if (vLayout != null)
            {
                components.Add(GetLayoutGroupInfo("VerticalLayoutGroup", vLayout));
            }

            GridLayoutGroup gridLayout = go.GetComponent<GridLayoutGroup>();
            if (gridLayout != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "GridLayoutGroup",
                    ["cellSize"] = new JObject { ["x"] = gridLayout.cellSize.x, ["y"] = gridLayout.cellSize.y },
                    ["spacing"] = new JObject { ["x"] = gridLayout.spacing.x, ["y"] = gridLayout.spacing.y },
                    ["startCorner"] = gridLayout.startCorner.ToString(),
                    ["startAxis"] = gridLayout.startAxis.ToString(),
                    ["childAlignment"] = gridLayout.childAlignment.ToString(),
                    ["constraint"] = gridLayout.constraint.ToString(),
                    ["constraintCount"] = gridLayout.constraintCount
                });
            }

            ContentSizeFitter csf = go.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "ContentSizeFitter",
                    ["horizontalFit"] = csf.horizontalFit.ToString(),
                    ["verticalFit"] = csf.verticalFit.ToString()
                });
            }

            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "LayoutElement",
                    ["ignoreLayout"] = le.ignoreLayout,
                    ["minWidth"] = le.minWidth,
                    ["minHeight"] = le.minHeight,
                    ["preferredWidth"] = le.preferredWidth,
                    ["preferredHeight"] = le.preferredHeight,
                    ["flexibleWidth"] = le.flexibleWidth,
                    ["flexibleHeight"] = le.flexibleHeight
                });
            }

            AspectRatioFitter arf = go.GetComponent<AspectRatioFitter>();
            if (arf != null)
            {
                components.Add(new JObject
                {
                    ["type"] = "AspectRatioFitter",
                    ["aspectMode"] = arf.aspectMode.ToString(),
                    ["aspectRatio"] = arf.aspectRatio
                });
            }

            info["uiComponents"] = components;

            // Children
            if (includeChildren && go.transform.childCount > 0)
            {
                JArray children = new JArray();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    Transform child = go.transform.GetChild(i);
                    children.Add(GetElementInfo(child.gameObject, true));
                }
                info["children"] = children;
            }

            return info;
        }

        private JObject GetLayoutGroupInfo(string type, HorizontalOrVerticalLayoutGroup layout)
        {
            return new JObject
            {
                ["type"] = type,
                ["padding"] = new JObject
                {
                    ["left"] = layout.padding.left,
                    ["right"] = layout.padding.right,
                    ["top"] = layout.padding.top,
                    ["bottom"] = layout.padding.bottom
                },
                ["spacing"] = layout.spacing,
                ["childAlignment"] = layout.childAlignment.ToString(),
                ["childControlWidth"] = layout.childControlWidth,
                ["childControlHeight"] = layout.childControlHeight,
                ["childForceExpandWidth"] = layout.childForceExpandWidth,
                ["childForceExpandHeight"] = layout.childForceExpandHeight
            };
        }
    }

    #endregion
}
