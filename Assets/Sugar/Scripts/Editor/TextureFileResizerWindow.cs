using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class TextureFileResizerWindow : EditorWindow
{
    private const int DefaultMaxDimension = 2048;
    private const int DefaultJpgQuality = 92;
    private const string BackupFolderName = "TextureFileResizerBackups";
    private const string BackupFolderEditorPrefsKey = "TextureFileResizerWindow.BackupFolderPath";

    private int maxDimension = DefaultMaxDimension;
    private int jpgQuality = DefaultJpgQuality;
    private bool createBackups = true;
    private string backupFolderPath;
    private Vector2 scrollPosition;
    private string lastReport = "No resize has been run yet.";

    [MenuItem("Tools/Textures/Permanent Texture Resizer")]
    private static void Open()
    {
        GetWindow<TextureFileResizerWindow>("Texture Resizer");
    }

    private void OnEnable()
    {
        backupFolderPath = EditorPrefs.GetString(BackupFolderEditorPrefsKey, GetDefaultBackupFolder());
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Permanent Texture Resizer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This permanently rewrites supported source texture files. Back up or use source control before running it.",
            MessageType.Warning);

        maxDimension = EditorGUILayout.IntField("Maximum Dimension", maxDimension);
        maxDimension = Mathf.Max(32, maxDimension);

        jpgQuality = EditorGUILayout.IntSlider("JPG Quality", jpgQuality, 1, 100);
        createBackups = EditorGUILayout.Toggle("Create Backups", createBackups);

        DrawBackupFolderSelector();

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Resize Selected Textures/Folders", GUILayout.Height(32)))
            {
                ResizeSelected();
            }

            if (GUILayout.Button("Resize All Under Assets", GUILayout.Height(32)))
            {
                ResizeAllUnderAssets();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Supported file types: PNG, JPG, JPEG, TGA, EXR");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last Report", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.TextArea(lastReport, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void ResizeSelected()
    {
        List<string> paths = GetSelectedTexturePaths();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Texture Resizer",
                "Select one or more texture assets or folders first.",
                "OK");
            return;
        }

        ConfirmAndResize(paths, "selected texture source files");
    }

    private void ResizeAllUnderAssets()
    {
        List<string> paths = GetTexturePathsUnder("Assets");
        ConfirmAndResize(paths, "supported texture source files under Assets");
    }

    private void ConfirmAndResize(List<string> paths, string description)
    {
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        bool confirmed = EditorUtility.DisplayDialog(
            "Permanently Resize Texture Files",
            $"This will scan {paths.Count} {description} and permanently rewrite files larger than {maxDimension}px on either side.\n\n" +
            $"Original files will be copied to this backup folder first if backups are enabled:\n{GetBackupFolderPath()}",
            "Resize Files",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        ResizeTextureFiles(paths);
    }

    private void ResizeTextureFiles(List<string> paths)
    {
        int resized = 0;
        int skippedSmall = 0;
        int skippedUnsupported = 0;
        int failed = 0;
        string backupFolder = createBackups ? GetBackupFolderPath() : null;
        string backupRoot = null;
        List<string> messages = new List<string>();

        if (createBackups && !IsUsableFolderPath(backupFolder))
        {
            EditorUtility.DisplayDialog(
                "Choose Backup Folder",
                "The backup folder path is not valid. Use Browse or Reset before resizing.",
                "OK");
            return;
        }

        if (!string.IsNullOrEmpty(backupFolder) && IsPathInside(backupFolder, Application.dataPath))
        {
            EditorUtility.DisplayDialog(
                "Choose Another Backup Folder",
                "The backup folder is inside Assets, so Unity would import the backup copies too. Choose a folder outside Assets.",
                "OK");
            return;
        }

        try
        {
            backupRoot = createBackups ? CreateBackupRoot(backupFolder) : null;
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Could Not Create Backup Folder",
                $"The backup folder could not be created.\n\n{exception.Message}",
                "OK");
            return;
        }

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string assetPath = paths[i];
                EditorUtility.DisplayProgressBar(
                    "Permanent Texture Resizer",
                    assetPath,
                    paths.Count == 0 ? 1f : (float)i / paths.Count);

                ResizeResult result = ResizeOneTexture(assetPath, backupRoot);
                switch (result.Status)
                {
                    case ResizeStatus.Resized:
                        resized++;
                        messages.Add($"Resized: {assetPath} ({result.OldWidth}x{result.OldHeight} -> {result.NewWidth}x{result.NewHeight})");
                        break;
                    case ResizeStatus.SkippedSmall:
                        skippedSmall++;
                        break;
                    case ResizeStatus.SkippedUnsupported:
                        skippedUnsupported++;
                        messages.Add($"Skipped unsupported type: {assetPath}");
                        break;
                    case ResizeStatus.Failed:
                        failed++;
                        messages.Add($"Failed: {assetPath}\n  {result.Message}");
                        break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        lastReport =
            $"Finished resizing texture source files.\n\n" +
            $"Resized: {resized}\n" +
            $"Already small enough: {skippedSmall}\n" +
            $"Unsupported: {skippedUnsupported}\n" +
            $"Failed: {failed}\n" +
            $"Backup folder: {(string.IsNullOrEmpty(backupRoot) ? "None" : backupRoot)}\n\n" +
            string.Join("\n", messages);

        EditorUtility.DisplayDialog("Texture Resizer", lastReport, "OK");
    }

    private void DrawBackupFolderSelector()
    {
        using (new EditorGUI.DisabledScope(!createBackups))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                backupFolderPath = EditorGUILayout.TextField("Backup Folder", GetBackupFolderPath());
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(BackupFolderEditorPrefsKey, backupFolderPath);
                }

                if (GUILayout.Button("Browse", GUILayout.Width(72)))
                {
                    string startingFolder = Directory.Exists(GetBackupFolderPath()) ? GetBackupFolderPath() : ProjectRoot;
                    string selectedFolder = EditorUtility.OpenFolderPanel("Choose Backup Folder", startingFolder, string.Empty);

                    if (!string.IsNullOrEmpty(selectedFolder))
                    {
                        backupFolderPath = Path.GetFullPath(selectedFolder);
                        EditorPrefs.SetString(BackupFolderEditorPrefsKey, backupFolderPath);
                    }
                }

                if (GUILayout.Button("Reset", GUILayout.Width(56)))
                {
                    backupFolderPath = GetDefaultBackupFolder();
                    EditorPrefs.SetString(BackupFolderEditorPrefsKey, backupFolderPath);
                }
            }

            if (createBackups && IsPathInside(GetBackupFolderPath(), Application.dataPath))
            {
                EditorGUILayout.HelpBox(
                    "Choose a backup folder outside Assets so backup copies do not get imported into the project.",
                    MessageType.Warning);
            }

            if (createBackups && !IsUsableFolderPath(GetBackupFolderPath()))
            {
                EditorGUILayout.HelpBox(
                    "The backup folder path is not valid. Use Browse or Reset before resizing.",
                    MessageType.Error);
            }
        }
    }

    private ResizeResult ResizeOneTexture(string assetPath, string backupRoot)
    {
        if (!IsSupportedImagePath(assetPath))
        {
            return ResizeResult.Unsupported();
        }

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            return ResizeResult.Failed("Asset is not imported as a Texture2D.");
        }

        importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
        int largestSide = Mathf.Max(sourceWidth, sourceHeight);

        if (largestSide <= maxDimension)
        {
            return ResizeResult.Small();
        }

        int originalMaxTextureSize = importer.maxTextureSize;
        bool originalReadable = importer.isReadable;
        TextureImporterCompression originalCompression = importer.textureCompression;
        bool originalCrunched = importer.crunchedCompression;
        TextureImporterType originalTextureType = importer.textureType;
        bool originalSrgb = importer.sRGBTexture;
        bool wroteFile = false;

        try
        {
            importer.maxTextureSize = GetImporterMaxSize(largestSide);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (source == null)
            {
                return ResizeResult.Failed("Could not load the source texture after making it readable.");
            }

            int newWidth;
            int newHeight;
            GetResizedDimensions(source.width, source.height, maxDimension, out newWidth, out newHeight);

            Texture2D resizedTexture = ResizeTexture(source, newWidth, newHeight);
            byte[] encodedBytes = EncodeTexture(resizedTexture, assetPath, jpgQuality);
            DestroyImmediate(resizedTexture);

            if (encodedBytes == null || encodedBytes.Length == 0)
            {
                return ResizeResult.Failed("Could not encode the resized texture.");
            }

            if (!string.IsNullOrEmpty(backupRoot))
            {
                BackupAssetFile(assetPath, backupRoot);
            }

            File.WriteAllBytes(ToAbsolutePath(assetPath), encodedBytes);
            wroteFile = true;

            importer.maxTextureSize = Mathf.Min(originalMaxTextureSize, maxDimension);
            importer.sRGBTexture = originalSrgb;
            importer.textureType = originalTextureType;
            importer.isReadable = originalReadable;
            importer.textureCompression = originalCompression;
            importer.crunchedCompression = originalCrunched;
            importer.SaveAndReimport();

            return ResizeResult.Resized(sourceWidth, sourceHeight, newWidth, newHeight);
        }
        catch (Exception exception)
        {
            return ResizeResult.Failed(exception.Message);
        }
        finally
        {
            importer.maxTextureSize = wroteFile ? Mathf.Min(originalMaxTextureSize, maxDimension) : originalMaxTextureSize;
            importer.sRGBTexture = originalSrgb;
            importer.textureType = originalTextureType;
            importer.isReadable = originalReadable;
            importer.textureCompression = originalCompression;
            importer.crunchedCompression = originalCrunched;
            importer.SaveAndReimport();
        }
    }

    private static Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        Color32[] sourcePixels = source.GetPixels32();
        Color32[] resizedPixels = new Color32[width * height];
        float xRatio = (float)source.width / width;
        float yRatio = (float)source.height / height;

        for (int y = 0; y < height; y++)
        {
            float sourceY = (y + 0.5f) * yRatio - 0.5f;
            int y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, source.height - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, source.height - 1);
            float yLerp = Mathf.Clamp01(sourceY - y0);

            for (int x = 0; x < width; x++)
            {
                float sourceX = (x + 0.5f) * xRatio - 0.5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, source.width - 1);
                int x1 = Mathf.Clamp(x0 + 1, 0, source.width - 1);
                float xLerp = Mathf.Clamp01(sourceX - x0);

                Color32 topLeft = sourcePixels[y0 * source.width + x0];
                Color32 topRight = sourcePixels[y0 * source.width + x1];
                Color32 bottomLeft = sourcePixels[y1 * source.width + x0];
                Color32 bottomRight = sourcePixels[y1 * source.width + x1];

                Color32 top = Lerp(topLeft, topRight, xLerp);
                Color32 bottom = Lerp(bottomLeft, bottomRight, xLerp);
                resizedPixels[y * width + x] = Lerp(top, bottom, yLerp);
            }
        }

        Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
        resized.SetPixels32(resizedPixels);
        resized.Apply(false, false);
        return resized;
    }

    private static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        return new Color32(
            LerpByte(a.r, b.r, t),
            LerpByte(a.g, b.g, t),
            LerpByte(a.b, b.b, t),
            LerpByte(a.a, b.a, t));
    }

    private static byte LerpByte(byte a, byte b, float t)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt(a + (b - a) * t), 0, 255);
    }

    private static byte[] EncodeTexture(Texture2D texture, string assetPath, int quality)
    {
        string extension = Path.GetExtension(assetPath).ToLowerInvariant();
        switch (extension)
        {
            case ".png":
                return texture.EncodeToPNG();
            case ".jpg":
            case ".jpeg":
                return texture.EncodeToJPG(quality);
            case ".tga":
                return EncodeToTga(texture);
            case ".exr":
                return texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            default:
                return null;
        }
    }

    private static byte[] EncodeToTga(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        int width = texture.width;
        int height = texture.height;
        byte[] bytes = new byte[18 + width * height * 4];

        bytes[2] = 2;
        bytes[12] = (byte)(width & 0xFF);
        bytes[13] = (byte)((width >> 8) & 0xFF);
        bytes[14] = (byte)(height & 0xFF);
        bytes[15] = (byte)((height >> 8) & 0xFF);
        bytes[16] = 32;
        bytes[17] = 40;

        int index = 18;
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[y * width + x];
                bytes[index++] = pixel.b;
                bytes[index++] = pixel.g;
                bytes[index++] = pixel.r;
                bytes[index++] = pixel.a;
            }
        }

        return bytes;
    }

    private static void GetResizedDimensions(int width, int height, int maxDimension, out int newWidth, out int newHeight)
    {
        float scale = (float)maxDimension / Mathf.Max(width, height);
        newWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        newHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
    }

    private static int GetImporterMaxSize(int requiredSize)
    {
        int[] sizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
        for (int i = 0; i < sizes.Length; i++)
        {
            if (sizes[i] >= requiredSize)
            {
                return sizes[i];
            }
        }

        return sizes[sizes.Length - 1];
    }

    private static List<string> GetSelectedTexturePaths()
    {
        HashSet<string> paths = new HashSet<string>();

        foreach (UnityEngine.Object selectedObject in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (Directory.Exists(ToAbsolutePath(path)))
            {
                foreach (string texturePath in GetTexturePathsUnder(path))
                {
                    paths.Add(texturePath);
                }
            }
            else if (IsSupportedImagePath(path))
            {
                paths.Add(path);
            }
        }

        return new List<string>(paths);
    }

    private static List<string> GetTexturePathsUnder(string folder)
    {
        HashSet<string> paths = new HashSet<string>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsSupportedImagePath(path))
            {
                paths.Add(path);
            }
        }

        return new List<string>(paths);
    }

    private static bool IsSupportedImagePath(string assetPath)
    {
        string extension = Path.GetExtension(assetPath).ToLowerInvariant();
        return extension == ".png" ||
               extension == ".jpg" ||
               extension == ".jpeg" ||
               extension == ".tga" ||
               extension == ".exr";
    }

    private static string CreateBackupRoot(string backupFolder)
    {
        string root = Path.Combine(backupFolder, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void BackupAssetFile(string assetPath, string backupRoot)
    {
        string sourcePath = ToAbsolutePath(assetPath);
        string destinationPath = Path.Combine(backupRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        string destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, true);

        string metaPath = sourcePath + ".meta";
        if (File.Exists(metaPath))
        {
            File.Copy(metaPath, destinationPath + ".meta", true);
        }
    }

    private static string ToAbsolutePath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
    }

    private string GetBackupFolderPath()
    {
        if (string.IsNullOrWhiteSpace(backupFolderPath))
        {
            backupFolderPath = GetDefaultBackupFolder();
        }

        return backupFolderPath;
    }

    private static string GetDefaultBackupFolder()
    {
        return Path.Combine(ProjectRoot, BackupFolderName);
    }

    private static bool IsPathInside(string path, string parentPath)
    {
        if (!IsUsableFolderPath(path) || !IsUsableFolderPath(parentPath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullParentPath = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(fullParentPath, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullParentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullParentPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            Path.GetFullPath(path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    private static string ProjectRoot
    {
        get { return Directory.GetParent(Application.dataPath).FullName; }
    }

    private enum ResizeStatus
    {
        Resized,
        SkippedSmall,
        SkippedUnsupported,
        Failed
    }

    private struct ResizeResult
    {
        public ResizeStatus Status;
        public int OldWidth;
        public int OldHeight;
        public int NewWidth;
        public int NewHeight;
        public string Message;

        public static ResizeResult Resized(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            return new ResizeResult
            {
                Status = ResizeStatus.Resized,
                OldWidth = oldWidth,
                OldHeight = oldHeight,
                NewWidth = newWidth,
                NewHeight = newHeight
            };
        }

        public static ResizeResult Small()
        {
            return new ResizeResult { Status = ResizeStatus.SkippedSmall };
        }

        public static ResizeResult Unsupported()
        {
            return new ResizeResult { Status = ResizeStatus.SkippedUnsupported };
        }

        public static ResizeResult Failed(string message)
        {
            return new ResizeResult
            {
                Status = ResizeStatus.Failed,
                Message = message
            };
        }
    }
}
