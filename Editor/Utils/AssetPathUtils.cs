using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace McpUnity.Utils
{
    /// <summary>
    /// Shared validation and filesystem guards for Unity asset write paths.
    /// </summary>
    public static class AssetPathUtils
    {
        /// <summary>
        /// Normalize an explicit Unity asset path and prove that its full filesystem path
        /// is the project Assets directory or one of its descendants.
        /// </summary>
        public static bool TryNormalizeAssetPath(
            string assetPath,
            out string normalizedAssetPath,
            out string fullPath,
            out string errorMessage)
        {
            normalizedAssetPath = null;
            fullPath = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = "Asset path must not be empty.";
                return false;
            }

            string unityPath = assetPath.Replace('\\', '/');
            if (Path.IsPathRooted(assetPath) || Path.IsPathRooted(unityPath))
            {
                errorMessage = $"Asset path '{assetPath}' must be project-relative and inside 'Assets/'.";
                return false;
            }

            if (!string.Equals(unityPath, "Assets", StringComparison.Ordinal)
                && !unityPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                errorMessage = $"Asset path '{assetPath}' must explicitly start with 'Assets/'.";
                return false;
            }

            try
            {
                DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
                if (projectDirectory == null)
                {
                    errorMessage = "Could not resolve the Unity project root.";
                    return false;
                }

                string projectRoot = Path.GetFullPath(projectDirectory.FullName)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string assetsRoot = Path.GetFullPath(Application.dataPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string filesystemRelativePath = unityPath.Replace('/', Path.DirectorySeparatorChar);
                string candidatePath = Path.GetFullPath(Path.Combine(projectRoot, filesystemRelativePath));
                StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                string assetsPrefix = assetsRoot + Path.DirectorySeparatorChar;

                if (!string.Equals(candidatePath, assetsRoot, comparison)
                    && !candidatePath.StartsWith(assetsPrefix, comparison))
                {
                    errorMessage =
                        $"Asset path '{assetPath}' resolves outside this project's Assets directory.";
                    return false;
                }

                string relativePath = candidatePath.Substring(projectRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedAssetPath = relativePath
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                fullPath = candidatePath;
                return true;
            }
            catch (Exception ex) when (
                ex is ArgumentException
                || ex is NotSupportedException
                || ex is PathTooLongException)
            {
                errorMessage = $"Asset path '{assetPath}' is invalid: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Return true only when an existing file has its read-only attribute set.
        /// This guard never changes file attributes or calls source-control checkout APIs.
        /// </summary>
        public static bool IsExistingFileReadOnly(string fullPath)
        {
            return !string.IsNullOrEmpty(fullPath)
                && File.Exists(fullPath)
                && (File.GetAttributes(fullPath) & FileAttributes.ReadOnly) != 0;
        }

        /// <summary>
        /// Create a directory tree one segment at a time and report the highest directory
        /// actually created by this call. Existing files are never treated as missing folders.
        /// </summary>
        public static bool TryCreateOwnedDirectoryTree(
            string directoryPath,
            out string createdDirectoryRoot,
            out string errorMessage)
        {
            createdDirectoryRoot = null;
            errorMessage = null;
            if (string.IsNullOrEmpty(directoryPath) || Directory.Exists(directoryPath))
            {
                return true;
            }

            var missingDirectories = new Stack<string>();
            string current = directoryPath;
            while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
            {
                if (File.Exists(current))
                {
                    errorMessage =
                        $"Cannot create directory '{directoryPath}' because '{current}' " +
                        "is an existing file.";
                    return false;
                }

                missingDirectories.Push(current);
                string parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.Ordinal))
                {
                    errorMessage = $"Could not resolve a parent directory for '{directoryPath}'.";
                    return false;
                }
                current = parent;
            }

            try
            {
                while (missingDirectories.Count > 0)
                {
                    string next = missingDirectories.Pop();
                    if (Directory.Exists(next))
                    {
                        continue;
                    }
                    if (File.Exists(next))
                    {
                        throw new IOException(
                            $"Cannot create directory '{directoryPath}' because '{next}' " +
                            "is an existing file.");
                    }

                    Directory.CreateDirectory(next);
                    if (!Directory.Exists(next))
                    {
                        throw new IOException($"Directory creation did not produce '{next}'.");
                    }
                    if (string.IsNullOrEmpty(createdDirectoryRoot))
                    {
                        createdDirectoryRoot = next;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                string cleanupError = null;
                try
                {
                    DeleteOwnedDirectoryTree(createdDirectoryRoot);
                }
                catch (Exception cleanupException)
                {
                    cleanupError = cleanupException.Message;
                }

                createdDirectoryRoot = null;
                errorMessage = string.IsNullOrEmpty(cleanupError)
                    ? $"Could not create directory '{directoryPath}': {ex.Message}"
                    : $"Could not create directory '{directoryPath}': {ex.Message} " +
                      $"Cleanup also failed: {cleanupError}";
                return false;
            }
        }

        /// <summary>
        /// Delete only a directory root previously returned by TryCreateOwnedDirectoryTree,
        /// together with the folder meta file Unity may have created afterwards.
        /// </summary>
        public static void DeleteOwnedDirectoryTree(string createdDirectoryRoot)
        {
            if (string.IsNullOrEmpty(createdDirectoryRoot))
            {
                return;
            }
            if (Directory.Exists(createdDirectoryRoot))
            {
                Directory.Delete(createdDirectoryRoot, true);
            }
            if (File.Exists(createdDirectoryRoot + ".meta"))
            {
                File.Delete(createdDirectoryRoot + ".meta");
            }
        }
    }
}
