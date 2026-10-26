using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Unity.VisualStudio.Editor;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Подключение Cursor к генератору .csproj.
/// External Tools => «Cursor».
/// </summary>
[InitializeOnLoad]
public class CursorScriptEditor : IExternalCodeEditor
{
    static readonly CursorScriptEditor Instance = new();

    static CursorScriptEditor()
    {
        CodeEditor.Register(Instance);
    }

    public CodeEditor.Installation[] Installations
    {
        get
        {
            var path = FindCursorPath();
            if (string.IsNullOrEmpty(path))
                return Array.Empty<CodeEditor.Installation>();

            return new[]
            {
                new CodeEditor.Installation
                {
                    Name = "Cursor",
                    Path = path
                }
            };
        }
    }

    public void Initialize(string editorInstallationPath)
    {
    }

    public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
    {
        if (!IsCursorPath(editorPath))
        {
            installation = default;
            return false;
        }

        installation = new CodeEditor.Installation
        {
            Name = "Cursor",
            Path = editorPath
        };
        return true;
    }

    public void OnGUI()
    {
        var generator = GetSdkGenerator();
        if (generator == null)
        {
            EditorGUILayout.HelpBox(
                "Пакет Visual Studio Editor не найден. Без него нельзя сгенерировать .csproj.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Generate .csproj files for:");
        EditorGUI.indentLevel++;
        DrawFlag(generator, ProjectGenerationFlag.Embedded, "Embedded packages");
        DrawFlag(generator, ProjectGenerationFlag.Local, "Local packages");
        DrawFlag(generator, ProjectGenerationFlag.Registry, "Registry packages");
        DrawFlag(generator, ProjectGenerationFlag.Git, "Git packages");
        DrawFlag(generator, ProjectGenerationFlag.BuiltIn, "Built-in packages");
        DrawFlag(generator, ProjectGenerationFlag.LocalTarBall, "Local tarball");
        DrawFlag(generator, ProjectGenerationFlag.Unknown, "Packages from unknown sources");
        DrawFlag(generator, ProjectGenerationFlag.PlayerAssemblies, "Player projects");

        var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
        rect.width = 252;
        if (GUI.Button(rect, "Regenerate project files"))
            generator.Sync();

        EditorGUI.indentLevel--;
    }

    public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
    {
        var generator = GetSdkGenerator();
        if (generator == null)
            return;

        var affected = addedFiles
            .Concat(deletedFiles)
            .Concat(movedFiles)
            .Concat(movedFromFiles);
        generator.SyncIfNeeded(affected, importedFiles);
    }

    public void SyncAll()
    {
        GetSdkGenerator()?.Sync();
    }

    public bool OpenProject(string path, int line, int column)
    {
        var editorPath = CodeEditor.CurrentEditorInstallation;
        if (!IsCursorPath(editorPath))
            return false;

        var generator = GetSdkGenerator();
        generator?.Sync();

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            return false;

        line = Math.Max(1, line);
        column = Math.Max(0, column);

        // Поднятие solution.
        var args = string.IsNullOrEmpty(path)
            ? $"\"{projectRoot}\""
            : $"\"{projectRoot}\" -g \"{path}\":{line}:{column}";

        Process.Start(new ProcessStartInfo
        {
            FileName = editorPath,
            Arguments = args,
            UseShellExecute = false
        });
        return true;
    }

    static void DrawFlag(IGenerator generator, ProjectGenerationFlag flag, string label)
    {
        var provider = generator.AssemblyNameProvider;
        var previous = provider.ProjectGenerationFlag.HasFlag(flag);
        var next = EditorGUILayout.Toggle(label, previous);
        if (next != previous)
            provider.ToggleProjectGeneration(flag);
    }

    static IGenerator GetSdkGenerator()
    {
        // 1 = SDK, как у VS / VS Code.
        var assembly = typeof(VisualStudioEditor).Assembly;
        var factory = assembly.GetType("Microsoft.Unity.VisualStudio.Editor.GeneratorFactory");
        var styleType = assembly.GetType("Microsoft.Unity.VisualStudio.Editor.GeneratorStyle");
        if (factory == null || styleType == null)
            return null;

        var sdk = Enum.ToObject(styleType, 1);
        return factory.GetMethod("GetInstance")?.Invoke(null, new[] { sdk }) as IGenerator;
    }

    static bool IsCursorPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.Equals("Cursor", StringComparison.OrdinalIgnoreCase);
    }

    static string FindCursorPath()
    {
        var current = CodeEditor.CurrentEditorInstallation;
        if (IsCursorPath(current))
            return current;

        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localApp, "Programs", "cursor", "Cursor.exe"),
            Path.Combine(localApp, "Programs", "Cursor", "Cursor.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
