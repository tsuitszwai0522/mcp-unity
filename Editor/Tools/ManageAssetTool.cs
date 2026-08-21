using System;
using System.IO;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace McpUnity.Tools
{
    /// <summary>
    /// Moves, copies, renames, or creates folders for assets under this project's Assets folder.
    /// </summary>
    public class ManageAssetTool : McpToolBase
    {
        private const string ValidActions = "move, copy, rename, create_folder";

        public ManageAssetTool()
        {
            Name = "manage_asset";
            Description =
                "Moves, copies, or renames an existing asset, or creates one folder, at explicit " +
                "paths inside this project's Assets directory. Overwrite is not supported and " +
                "destination parent folders must already exist. Move and rename preserve the " +
                "source GUID; copy creates and reports a new GUID. Move and copy reject a " +
                "destination equal to or inside the source.";
        }

        public override JObject Execute(JObject parameters)
        {
            JToken actionToken = parameters?["action"];
            string action = actionToken != null && actionToken.Type == JTokenType.String
                ? actionToken.Value<string>()
                : null;
            if (!IsValidAction(action))
            {
                string received = actionToken == null || actionToken.Type == JTokenType.Null
                    ? "<missing>"
                    : actionToken.Type == JTokenType.String
                        ? action
                        : actionToken.ToString(Newtonsoft.Json.Formatting.None);
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Action '{received}' is invalid. Valid values: {ValidActions}",
                    "validation_error");
            }

            foreach (JProperty property in parameters.Properties())
            {
                if (property.Name != "action"
                    && property.Name != "assetPath"
                    && property.Name != "destinationPath"
                    && property.Name != "newName")
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown parameter '{property.Name}'. Valid parameters: action, " +
                        "assetPath, destinationPath, newName.",
                        "validation_error");
                }
            }

            bool hasDestinationPath = parameters.Property("destinationPath") != null;
            bool hasNewName = parameters.Property("newName") != null;
            if ((action == "move" || action == "copy") && hasNewName)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'newName' is only valid for action 'rename'.",
                    "validation_error");
            }
            if ((action == "rename" || action == "create_folder") && hasDestinationPath)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'destinationPath' is only valid for actions 'move' and 'copy'.",
                    "validation_error");
            }
            if (action == "create_folder" && hasNewName)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'newName' is only valid for action 'rename'.",
                    "validation_error");
            }

            JToken assetPathToken = parameters?["assetPath"];
            if (assetPathToken != null && assetPathToken.Type != JTokenType.String)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'assetPath' must be a string.",
                    "validation_error");
            }
            string requestedAssetPath = assetPathToken?.Value<string>();
            if (!AssetPathUtils.TryNormalizeAssetPath(
                    requestedAssetPath,
                    out string assetPath,
                    out string fullAssetPath,
                    out string assetPathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    assetPathError,
                    "validation_error");
            }

            string destinationPath = null;
            string fullDestinationPath = null;
            if (action == "move" || action == "copy")
            {
                JToken destinationPathToken = parameters?["destinationPath"];
                if (destinationPathToken != null
                    && destinationPathToken.Type != JTokenType.String)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Parameter 'destinationPath' must be a string.",
                        "validation_error");
                }
                string requestedDestinationPath = destinationPathToken?.Value<string>();
                if (!AssetPathUtils.TryNormalizeAssetPath(
                        requestedDestinationPath,
                        out destinationPath,
                        out fullDestinationPath,
                        out string destinationPathError))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"destinationPath: {destinationPathError}",
                        "validation_error");
                }
            }

            string newName = null;
            if (action == "rename")
            {
                JToken newNameToken = parameters?["newName"];
                if (newNameToken != null && newNameToken.Type != JTokenType.String)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Parameter 'newName' must be a string.",
                        "validation_error");
                }
                newName = newNameToken?.Value<string>();
                if (string.IsNullOrWhiteSpace(newName)
                    || !string.Equals(newName.Trim(), newName, StringComparison.Ordinal)
                    || newName.IndexOf('/') >= 0
                    || newName.IndexOf('\\') >= 0
                    || string.Equals(newName, ".", StringComparison.Ordinal)
                    || string.Equals(newName, "..", StringComparison.Ordinal)
                    || newName.EndsWith(".", StringComparison.Ordinal)
                    || newName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Parameter 'newName' must not be empty, have leading or trailing " +
                        "whitespace, contain '/' or '\\', equal '.' or '..', or end in '.' or " +
                        "'.meta'.",
                        "validation_error");
                }
            }

            if (action == "create_folder")
            {
                return CreateFolder(assetPath, fullAssetPath);
            }

            if (!AssetExists(assetPath, fullAssetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Source asset was not found at '{assetPath}'.",
                    "not_found_error");
            }

            string sourceGuid = AssetDatabase.AssetPathToGUID(
                assetPath,
                AssetPathToGUIDOptions.OnlyExistingAssets);
            if (string.IsNullOrEmpty(sourceGuid))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not read the GUID for source asset '{assetPath}'.",
                    "tool_execution_error");
            }

            if ((action == "move" || action == "copy")
                && (string.Equals(
                        destinationPath,
                        assetPath,
                        StringComparison.OrdinalIgnoreCase)
                    || destinationPath.StartsWith(
                        assetPath + "/",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Destination '{destinationPath}' is case-insensitively inside the source " +
                    $"folder '{assetPath}' or is the source itself.",
                    "validation_error");
            }

            if ((action == "move" || action == "rename")
                && (AssetPathUtils.IsExistingFileReadOnly(fullAssetPath)
                    || AssetPathUtils.IsExistingFileReadOnly(fullAssetPath + ".meta")))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot {action} '{assetPath}' because the source asset or its meta file " +
                    "is read-only.",
                    "tool_execution_error");
            }

            if (action == "rename")
            {
                return RenameAsset(assetPath, sourceGuid, newName);
            }

            if (File.Exists(fullDestinationPath + ".meta")
                && !File.Exists(fullDestinationPath)
                && !Directory.Exists(fullDestinationPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot use destination '{destinationPath}' because a .meta file already " +
                    "exists at destination.",
                    "validation_error");
            }

            if (AssetExists(destinationPath, fullDestinationPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"destination already exists; overwrite is not supported: " +
                    $"'{destinationPath}'.",
                    "validation_error");
            }

            string destinationParent = GetParentAssetPath(destinationPath);
            if (string.IsNullOrEmpty(destinationParent)
                || !AssetDatabase.IsValidFolder(destinationParent))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Destination parent folder '{destinationParent}' does not exist. " +
                    "Create it first with manage_asset action 'create_folder'.",
                    "validation_error");
            }

            return action == "move"
                ? MoveAsset(assetPath, destinationPath, sourceGuid)
                : CopyAsset(
                    assetPath,
                    destinationPath,
                    fullDestinationPath,
                    sourceGuid);
        }

        private static JObject MoveAsset(
            string assetPath,
            string destinationPath,
            string sourceGuid)
        {
            string validationError = AssetDatabase.ValidateMoveAsset(
                assetPath,
                destinationPath);
            if (!string.IsNullOrEmpty(validationError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Unity rejected moving '{assetPath}' to '{destinationPath}': " +
                    validationError,
                    "validation_error");
            }

            try
            {
                string moveError = AssetDatabase.MoveAsset(assetPath, destinationPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    return CreateMoveFailureWithRollback(
                        assetPath,
                        sourceGuid,
                        $"Unity failed to move '{assetPath}' to '{destinationPath}': {moveError}");
                }

                string actualPath = AssetDatabase.GUIDToAssetPath(sourceGuid);
                if (string.IsNullOrEmpty(actualPath)
                    || !string.Equals(
                        actualPath,
                        destinationPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return CreateMoveFailureWithRollback(
                        assetPath,
                        sourceGuid,
                        $"Move read-back for GUID '{sourceGuid}' returned " +
                        $"'{actualPath}' instead of '{destinationPath}'.");
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["action"] = "move",
                    ["message"] =
                        $"Moved asset to '{actualPath}' while preserving GUID '{sourceGuid}'.",
                    ["guid"] = sourceGuid,
                    ["assetPath"] = actualPath
                };
            }
            catch (Exception ex)
            {
                return CreateMoveFailureWithRollback(
                    assetPath,
                    sourceGuid,
                    $"Failed to move '{assetPath}' to '{destinationPath}': {ex.Message}");
            }
        }

        private static JObject RenameAsset(
            string assetPath,
            string sourceGuid,
            string newName)
        {
            string canonicalSourcePath = AssetDatabase.GUIDToAssetPath(sourceGuid);
            string expectedPath = GetParentAssetPath(canonicalSourcePath) + "/" + newName;
            if (!AssetDatabase.IsValidFolder(canonicalSourcePath))
                expectedPath += Path.GetExtension(canonicalSourcePath);

            try
            {
                string renameError = AssetDatabase.RenameAsset(assetPath, newName);
                if (!string.IsNullOrEmpty(renameError))
                {
                    return CreateMoveFailureWithRollback(
                        assetPath,
                        sourceGuid,
                        $"Unity failed to rename '{assetPath}' to '{newName}': {renameError}");
                }

                string actualPath = AssetDatabase.GUIDToAssetPath(sourceGuid);
                if (string.IsNullOrEmpty(actualPath)
                    || !string.Equals(
                        actualPath,
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return CreateMoveFailureWithRollback(
                        assetPath,
                        sourceGuid,
                        $"Rename read-back for GUID '{sourceGuid}' returned '{actualPath}' " +
                        $"instead of '{expectedPath}'.");
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["action"] = "rename",
                    ["message"] =
                        $"Renamed asset to '{actualPath}' while preserving GUID '{sourceGuid}'.",
                    ["guid"] = sourceGuid,
                    ["assetPath"] = actualPath
                };
            }
            catch (Exception ex)
            {
                return CreateMoveFailureWithRollback(
                    assetPath,
                    sourceGuid,
                    $"Failed to rename '{assetPath}' to '{newName}': {ex.Message}");
            }
        }

        private static JObject CopyAsset(
            string assetPath,
            string destinationPath,
            string fullDestinationPath,
            string sourceGuid)
        {
            bool destinationFileExisted = File.Exists(fullDestinationPath);
            bool destinationDirectoryExisted = Directory.Exists(fullDestinationPath);
            bool destinationMetaExisted = File.Exists(fullDestinationPath + ".meta");
            try
            {
                if (!AssetDatabase.CopyAsset(assetPath, destinationPath))
                {
                    return CreateOwnedTargetFailure(
                        destinationPath,
                        fullDestinationPath,
                        destinationFileExisted,
                        destinationDirectoryExisted,
                        destinationMetaExisted,
                        $"Unity failed to copy '{assetPath}' to '{destinationPath}'.");
                }

                string copyGuid = AssetDatabase.AssetPathToGUID(
                    destinationPath,
                    AssetPathToGUIDOptions.OnlyExistingAssets);
                string actualCopyPath = string.IsNullOrEmpty(copyGuid)
                    ? null
                    : AssetDatabase.GUIDToAssetPath(copyGuid);
                string actualSourcePath = AssetDatabase.GUIDToAssetPath(sourceGuid);
                if (string.IsNullOrEmpty(copyGuid)
                    || string.Equals(copyGuid, sourceGuid, StringComparison.Ordinal)
                    || !string.Equals(
                        actualCopyPath,
                        destinationPath,
                        StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(actualSourcePath))
                {
                    return CreateOwnedTargetFailure(
                        destinationPath,
                        fullDestinationPath,
                        destinationFileExisted,
                        destinationDirectoryExisted,
                        destinationMetaExisted,
                        $"Copy read-back failed for source GUID '{sourceGuid}' and destination " +
                        $"'{destinationPath}'.");
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["action"] = "copy",
                    ["message"] =
                        $"Copied '{actualSourcePath}' to '{actualCopyPath}' with new GUID " +
                        $"'{copyGuid}'; source GUID '{sourceGuid}' is unchanged.",
                    ["assetPath"] = actualCopyPath,
                    ["guid"] = copyGuid,
                    ["sourcePath"] = actualSourcePath,
                    ["sourceGuid"] = sourceGuid
                };
            }
            catch (Exception ex)
            {
                return CreateOwnedTargetFailure(
                    destinationPath,
                    fullDestinationPath,
                    destinationFileExisted,
                    destinationDirectoryExisted,
                    destinationMetaExisted,
                    $"Failed to copy '{assetPath}' to '{destinationPath}': {ex.Message}");
            }
        }

        private static JObject CreateFolder(string assetPath, string fullAssetPath)
        {
            if (File.Exists(fullAssetPath + ".meta")
                && !File.Exists(fullAssetPath)
                && !Directory.Exists(fullAssetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot create folder '{assetPath}' because a .meta file already exists " +
                    "at destination.",
                    "validation_error");
            }

            if (AssetExists(assetPath, fullAssetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"destination already exists; overwrite is not supported: '{assetPath}'.",
                    "validation_error");
            }

            string parentFolder = GetParentAssetPath(assetPath);
            if (string.IsNullOrEmpty(parentFolder)
                || !AssetDatabase.IsValidFolder(parentFolder))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Parent folder '{parentFolder}' does not exist. Create it first with " +
                    "manage_asset action 'create_folder'.",
                    "validation_error");
            }

            string folderName = GetAssetName(assetPath);
            bool destinationFileExisted = File.Exists(fullAssetPath);
            bool destinationDirectoryExisted = Directory.Exists(fullAssetPath);
            bool destinationMetaExisted = File.Exists(fullAssetPath + ".meta");
            try
            {
                string guid = AssetDatabase.CreateFolder(parentFolder, folderName);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateOwnedTargetFailure(
                        assetPath,
                        fullAssetPath,
                        destinationFileExisted,
                        destinationDirectoryExisted,
                        destinationMetaExisted,
                        $"Unity failed to create folder '{assetPath}'.");
                }

                string actualPath = AssetDatabase.GUIDToAssetPath(guid);
                bool isValidFolder = AssetDatabase.IsValidFolder(actualPath);
                if (string.IsNullOrEmpty(actualPath)
                    || !string.Equals(
                        actualPath,
                        assetPath,
                        StringComparison.OrdinalIgnoreCase)
                    || !isValidFolder)
                {
                    return CreateOwnedTargetFailure(
                        assetPath,
                        fullAssetPath,
                        destinationFileExisted,
                        destinationDirectoryExisted,
                        destinationMetaExisted,
                        $"Folder read-back for GUID '{guid}' returned '{actualPath}' with " +
                        $"IsValidFolder={isValidFolder}.");
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["action"] = "create_folder",
                    ["message"] =
                        $"Created folder '{actualPath}' with GUID '{guid}'.",
                    ["guid"] = guid,
                    ["assetPath"] = actualPath,
                    ["isValidFolder"] = isValidFolder
                };
            }
            catch (Exception ex)
            {
                return CreateOwnedTargetFailure(
                    assetPath,
                    fullAssetPath,
                    destinationFileExisted,
                    destinationDirectoryExisted,
                    destinationMetaExisted,
                    $"Failed to create folder '{assetPath}': {ex.Message}");
            }
        }

        private static JObject CreateMoveFailureWithRollback(
            string originalPath,
            string sourceGuid,
            string failureMessage)
        {
            string rollbackError = null;
            try
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(sourceGuid);
                if (!string.IsNullOrEmpty(currentPath)
                    && !string.Equals(
                        currentPath,
                        originalPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    rollbackError = AssetDatabase.MoveAsset(currentPath, originalPath);
                }
            }
            catch (Exception rollbackException)
            {
                rollbackError = rollbackException.Message;
            }

            string message = string.IsNullOrEmpty(rollbackError)
                ? failureMessage
                : failureMessage + " Rollback also failed: " + rollbackError;
            return McpUnitySocketHandler.CreateErrorResponse(
                message,
                "tool_execution_error");
        }

        private static JObject CreateOwnedTargetFailure(
            string assetPath,
            string fullAssetPath,
            bool destinationFileExisted,
            bool destinationDirectoryExisted,
            bool destinationMetaExisted,
            string failureMessage)
        {
            string cleanupError = null;
            try
            {
                if (!destinationFileExisted
                    && !destinationDirectoryExisted
                    && !destinationMetaExisted)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                if (!destinationFileExisted && File.Exists(fullAssetPath))
                    File.Delete(fullAssetPath);
                else if (!destinationDirectoryExisted && Directory.Exists(fullAssetPath))
                    Directory.Delete(fullAssetPath, true);
                if (!destinationMetaExisted && File.Exists(fullAssetPath + ".meta"))
                    File.Delete(fullAssetPath + ".meta");

                if ((!destinationFileExisted && File.Exists(fullAssetPath))
                    || (!destinationDirectoryExisted && Directory.Exists(fullAssetPath))
                    || (!destinationMetaExisted && File.Exists(fullAssetPath + ".meta")))
                {
                    cleanupError = $"Artifacts remain at '{assetPath}'.";
                }
            }
            catch (Exception cleanupException)
            {
                cleanupError = cleanupException.Message;
            }

            string message = string.IsNullOrEmpty(cleanupError)
                ? failureMessage
                : failureMessage + " Cleanup also failed: " + cleanupError;
            return McpUnitySocketHandler.CreateErrorResponse(
                message,
                "tool_execution_error");
        }

        private static bool IsValidAction(string action)
        {
            return action == "move"
                || action == "copy"
                || action == "rename"
                || action == "create_folder";
        }

        private static bool AssetExists(string assetPath, string fullAssetPath)
        {
            return AssetDatabase.IsValidFolder(assetPath)
                || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(
                    assetPath,
                    AssetPathToGUIDOptions.OnlyExistingAssets))
                || File.Exists(fullAssetPath)
                || Directory.Exists(fullAssetPath);
        }

        private static string GetParentAssetPath(string assetPath)
        {
            int separatorIndex = assetPath.LastIndexOf('/');
            return separatorIndex <= 0 ? null : assetPath.Substring(0, separatorIndex);
        }

        private static string GetAssetName(string assetPath)
        {
            int separatorIndex = assetPath.LastIndexOf('/');
            return separatorIndex < 0 ? assetPath : assetPath.Substring(separatorIndex + 1);
        }
    }
}
