#if UNITY_EDITOR && UNITY_ANDROID

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;
using Xsolla.SDK.Common;

namespace Xsolla.SDK
{
    public class XsollaGradleInjector : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;

        private static readonly Lazy<UTF8Encoding> utf8EncodingLazy = new(() =>
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        private const string INJECTION_START_MARKER = "// XSOLLA_GRADLE_INJECTION_START_DO_NOT_EDIT";
        private const string INJECTION_END_MARKER = "// XSOLLA_GRADLE_INJECTION_END";
        private const string SCRIPT_PREFIX = "xsolla_";
        private const string GRADLE_SCRIPTS_FOLDER = "GradleScripts";

        protected static XsollaClientSettingsAsset TryToLoad()
        {
            XsollaClientSettingsAsset xsollaClientSettingsAsset = Resources.Load("XsollaSDKSettings") as XsollaClientSettingsAsset;

            return xsollaClientSettingsAsset;
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                var settingsAsset = TryToLoad();
                if (settingsAsset == null || !settingsAsset.settings.isEnable)
                    return;

                path = path.Replace('\\', '/');

                var projectName = Path.GetFileName(path);

                var gradleFileBasePath = projectName switch
                {
                    "launcher" => path,
                    "unityLibrary" => Path.Combine(path, "..", "launcher"),
                    _ => Path.Combine(path, "launcher")
                };

                Debug.Log($"[Xsolla] Starting Gradle script injection: {gradleFileBasePath}");

                var gradleFile = Path.Combine(gradleFileBasePath, "build.gradle");
                if (!File.Exists(gradleFile))
                {
                    Debug.LogWarning("[Xsolla] build.gradle not found, skipping injection");
                    return;
                }

                var scriptsFolder = FindGradleScriptsFolder();
                if (string.IsNullOrEmpty(scriptsFolder))
                {
                    Debug.LogWarning("[Xsolla] GradleScripts folder not found");
                    return;
                }

                var scriptsToInject = GetValidGradleScripts(scriptsFolder);
                if (scriptsToInject.Count == 0)
                {
                    Debug.Log("[Xsolla] No gradle scripts to inject");
                    return;
                }

                InjectScripts(gradleFile, gradleFileBasePath, scriptsToInject);

                Debug.Log($"[Xsolla] Gradle injection completed successfully: {gradleFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Xsolla] Gradle injection failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static string FindGradleScriptsFolder()
        {
            var guids = AssetDatabase.FindAssets($"t:Script {nameof(XsollaGradleInjector)}");

            foreach (var guid in guids)
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(guid);

                if (!scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Path.GetFileName(scriptPath).Equals($"{nameof(XsollaGradleInjector)}.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var editorDir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');

                if (!string.IsNullOrEmpty(editorDir))
                {
                    var candidate = $"{editorDir}/{GRADLE_SCRIPTS_FOLDER}";
                    if (AssetDatabase.IsValidFolder(candidate))
                    {
                        Debug.Log($"[Xsolla] Found GradleScripts: {candidate}");
                        return Path.GetFullPath(candidate);
                    }
                }
            }

            var allAssets = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.EndsWith($"/{GRADLE_SCRIPTS_FOLDER}", StringComparison.OrdinalIgnoreCase))
                .Where(p => p.Contains("Editor", StringComparison.OrdinalIgnoreCase))
                // Make sure it's "us" and not some first found similar folder.
                .Where(p => p.Contains("Xsolla", StringComparison.OrdinalIgnoreCase) ||
                    p.Contains("xsolla", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (allAssets.Count > 0)
            {
                var assetPath = allAssets[0];
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    Debug.Log($"[Xsolla] Found GradleScripts via search: {assetPath}");
                    return Path.GetFullPath(assetPath);
                }
            }

            Debug.LogError("[Xsolla] Failed to locate GradleScripts folder");
            return null;
        }

        private static List<string> GetValidGradleScripts(string folder)
        {
            var validScripts = new List<string>();

            if (!Directory.Exists(folder))
                return validScripts;

            try
            {
                var allFiles = Directory.GetFiles(folder, "*.gradle", SearchOption.TopDirectoryOnly);

                foreach (var file in allFiles)
                {
                    if (ValidateGradleScript(file))
                        validScripts.Add(file);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Xsolla] Failed to scan gradle scripts: {ex.Message}");
            }

            validScripts.Sort(StringComparer.OrdinalIgnoreCase);

            return validScripts;
        }

        private static bool ValidateGradleScript(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            // Skip disabled scripts (prefix with _ or ~).
            if (fileName.StartsWith("_") || fileName.StartsWith("~") || fileName.StartsWith("."))
            {
                Debug.Log($"[Xsolla] Skipping disabled script: {fileName}");
                return false;
            }

            // Skip backup files.
            if (fileName.Contains(".bak") || fileName.Contains(".backup") ||
                fileName.Contains(".old") || fileName.EndsWith("~"))
            {
                Debug.Log($"[Xsolla] Skipping backup file: {fileName}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);

                if (fileInfo.Length == 0)
                {
                    Debug.LogWarning($"[Xsolla] Skipping empty file: {fileName}");
                    return false;
                }

                var content = File.ReadAllText(filePath);

                // Must contain gradle syntax..
                var hasGradleKeywords =
                    content.Contains("android") ||
                    content.Contains("dependencies") ||
                    content.Contains("afterEvaluate") ||
                    content.Contains("task") ||
                    content.Contains("apply") ||
                    content.Contains("ext");

                if (!hasGradleKeywords)
                {
                    Debug.LogWarning($"[Xsolla] Skipping file without gradle syntax: {fileName}");
                    return false;
                }

                // Security check: block dangerous patterns.
                var dangerousPatterns = new[] {
                    "Runtime.getRuntime().exec",
                    "ProcessBuilder",
                    "curl", "wget",
                    "rm -rf", "del /f",
                    "format ", "mkfs",
                    // path traversal
                    "../"
                };

                foreach (var pattern in dangerousPatterns)
                {
                    if (content.Contains(pattern))
                    {
                        Debug.LogError($"[Xsolla] SECURITY: Rejected script with dangerous pattern '{pattern}': {fileName}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Xsolla] Failed to validate {fileName}: {ex.Message}");
                return false;
            }
        }

        private static void InjectScripts(string gradleFile, string exportPath, List<string> scriptsToInject)
        {
            try
            {
                var gradleContent = File.ReadAllText(gradleFile);

                // Remove any previous injection (idempotent).
                gradleContent = RemovePreviousInjection(gradleContent);

                var injectionBlock = new StringBuilder();
                injectionBlock.AppendLine();
                injectionBlock.AppendLine(INJECTION_START_MARKER);
                injectionBlock.AppendLine("// Auto-injected by Xsolla Unity SDK Plugin");
                injectionBlock.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var successCount = 0;

                foreach (var scriptPath in scriptsToInject)
                {
                    var originalName = Path.GetFileName(scriptPath);
                    var namespacedName = $"{SCRIPT_PREFIX}{originalName}";
                    var destPath = Path.Combine(exportPath, namespacedName);

                    try
                    {


                        // Copy Gradle script to exported project.
                        File.Copy(scriptPath, destPath, true);

                        if (!File.Exists(destPath))
                        {
                            Debug.LogError($"[Xsolla] Copy verification failed: {originalName}");
                            continue;
                        }

                        var sourceSize = new FileInfo(scriptPath).Length;
                        var destSize = new FileInfo(destPath).Length;

                        if (sourceSize != destSize)
                        {
                            Debug.LogError($"[Xsolla] Size mismatch after copy: {originalName}");
                            File.Delete(destPath);
                            continue;
                        }

                        injectionBlock.AppendLine($"apply from: '{namespacedName}'");
                        successCount++;

                        Debug.Log($"[Xsolla] ✓ Injected: {originalName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Xsolla] Failed to inject {originalName}: {ex.Message}");
                    }
                }

                if (successCount == 0)
                {
                    Debug.LogWarning("[Xsolla] No scripts were successfully injected");
                    return;
                }

                injectionBlock.AppendLine(INJECTION_END_MARKER);
                gradleContent += injectionBlock.ToString();

                File.WriteAllText(gradleFile, gradleContent, utf8EncodingLazy.Value);

                Debug.Log($"[Xsolla] ✓ Successfully injected {successCount}/{scriptsToInject.Count} gradle scripts");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Xsolla] Injection failed: {ex.Message}");
                throw;
            }
        }

        private static string RemovePreviousInjection(string content)
        {
            var startIndex = content.IndexOf(INJECTION_START_MARKER, StringComparison.Ordinal);
            if (startIndex == -1)
                return content;

            var endIndex = content.IndexOf(INJECTION_END_MARKER, startIndex, StringComparison.Ordinal);
            if (endIndex == -1)
            {
                Debug.LogWarning("[Xsolla] Found start marker but no end marker, skipping removal");
                return content;
            }

            // Include the end marker line.
            endIndex = content.IndexOf('\n', endIndex);
            if (endIndex == -1)
                endIndex = content.Length;
            else
                endIndex++;

            return content.Remove(startIndex, endIndex - startIndex);
        }
    }
}

#endif // UNITY_EDITOR && UNITY_ANDROID