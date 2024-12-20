#!/usr/bin/env dotnet-script

// This scripts is used to sync the version of Unity packages and sample apps with the version of NuGet packages.
// It gets the version from the Directory.Build.props file and updates
//  - the version in C# where the version field is marked with [VersionMarker] attribute
//  - the version in ProjectSettings.asset of the Unity sample app
//  - the version and dependency versions in package.json of all Unity packages
//  - the version in packages-lock.json of all Unity packages in the sample app

#load "get-version.csx"
#load "logging.csx"

#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.12.0"
#r "nuget: Newtonsoft.Json, 13.0.0"

using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var srcPath = Path.GetFullPath("src");
Log.Header("Starting Unity Version Sync");
Log.Info($"Source path: {srcPath}");

var newVersion = GetVersion();
Log.Info($"Target version: {newVersion}");

// Update version fields in the code
Log.SubHeader("Updating Version Fields in Code");
var versionFields = await FindVersionFieldsAsync(srcPath);
if (!versionFields.Any())
{
    Log.Warning("No version fields found");
}
foreach (var field in versionFields)
{
    Log.Info($"Found: {field.fieldName} = {field.version} in {Path.GetRelativePath(".", field.filePath)}");
    await UpdateVersionAsync(field.filePath, field.fieldName, newVersion);
    Log.Success($"Updated {field.fieldName} to {newVersion}");
}

// Update Unity sample app version
Log.SubHeader("Updating Unity Sample App Version");
var projectSettingsPath = Path.GetFullPath("sample/Reown.AppKit.Unity/ProjectSettings/ProjectSettings.asset");
if (File.Exists(projectSettingsPath))
{
    await UpdateProjectSettingsVersionAsync(projectSettingsPath, newVersion);
    Log.Success($"Updated ProjectSettings.asset to {newVersion}");
}
else
{
    Log.Warning($"ProjectSettings.asset not found at {projectSettingsPath}");
}

// Update Unity packages
Log.SubHeader("Updating Package Versions");
await UpdatePackageVersionsAsync(srcPath, newVersion);
await UpdatePackagesLockVersionsAsync(Path.GetFullPath("sample"), newVersion);

private static async Task<IEnumerable<(string filePath, string fieldName, string version)>> FindVersionFieldsAsync(string path)
{
    var results = new List<(string filePath, string fieldName, string version)>();
    foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
    {
        var sourceText = SourceText.From(await File.ReadAllTextAsync(file));
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = await tree.GetRootAsync();

        var fields = root.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Where(field => field.AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(attr => attr.Name.ToString() == "VersionMarker"));

        foreach (var field in fields)
        {
            var variable = field.Declaration.Variables.First();
            if (variable.Initializer?.Value is LiteralExpressionSyntax literal)
            {
                results.Add((
                    filePath: file,
                    fieldName: variable.Identifier.Text,
                    version: literal.Token.ValueText
                ));
            }
        }
    }

    return results;
}

async Task UpdateVersionAsync(string filePath, string fieldName, string newVersion)
{
    var sourceText = SourceText.From(await File.ReadAllTextAsync(filePath));
    var tree = CSharpSyntaxTree.ParseText(sourceText);
    var root = await tree.GetRootAsync();

    // Find the specific field to update
    var fieldToUpdate = root.DescendantNodes()
        .OfType<FieldDeclarationSyntax>()
        .First(field =>
            field.AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(attr => attr.Name.ToString() == "VersionMarker") &&
            field.Declaration.Variables
                .Any(v => v.Identifier.Text == fieldName));

    var variable = fieldToUpdate.Declaration.Variables.First();

    if (variable.Initializer?.Value is LiteralExpressionSyntax literal)
    {
        var currentValue = literal.Token.ValueText;
        var newValue = UpdateVersionString(currentValue, newVersion);

        // Create new string literal
        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(newValue)
        );

        // Create new variable with updated initializer
        var newVariable = variable.WithInitializer(
            SyntaxFactory.EqualsValueClause(newLiteral)
        );

        // Create new field declaration
        var newField = fieldToUpdate.WithDeclaration(
            fieldToUpdate.Declaration.WithVariables(
                SyntaxFactory.SingletonSeparatedList(newVariable)
            )
        );

        // Replace the old field with the new one
        var newRoot = root.ReplaceNode(fieldToUpdate, newField);

        // Preserve original formatting
        var formattedRoot = newRoot.NormalizeWhitespace();

        await File.WriteAllTextAsync(filePath, formattedRoot.ToFullString());
    }
}

string UpdateVersionString(string currentValue, string newVersion)
{
    var semVerPattern = new Regex(
        @"^(?<prefix>.*?)?(?:v)?(?<version>\d+\.\d+\.\d+)$",
        RegexOptions.Compiled
    );
    var match = semVerPattern.Match(currentValue);
    if (!match.Success)
    {
        throw new ArgumentException(
            $"Current value '{currentValue}' doesn't contain a valid version number pattern");
    }

    var prefix = match.Groups["prefix"].Value;
    var hasV = currentValue.Contains('v') &&
               (prefix.Length == 0 || !prefix.EndsWith('v'));

    return $"{prefix}{(hasV ? "v" : "")}{newVersion}";
}

async Task UpdateProjectSettingsVersionAsync(string filePath, string newVersion)
{
    if (!File.Exists(filePath))
    {
        Log.Warning($"ProjectSettings.asset not found at {filePath}");
        return;
    }

    var content = await File.ReadAllTextAsync(filePath);

    var pattern = @"(bundleVersion:)\s*(.+)";
    var replacement = $"$1 {newVersion}";

    var updatedContent = Regex.Replace(content, pattern, replacement);

    await File.WriteAllTextAsync(filePath, updatedContent);
    Log.Success($"Updated bundleVersion in ProjectSettings.asset to {newVersion}");
}

async Task UpdatePackageVersionsAsync(string srcPath, string newVersion)
{
    var files = Directory.EnumerateFiles(srcPath, "package.json", SearchOption.AllDirectories).ToList();
    if (!files.Any())
    {
        Log.Warning($"No package.json files found in {Path.GetRelativePath(".", srcPath)}");
        return;
    }

    foreach (var file in files)
    {
        Log.Info($"Processing: {Path.GetRelativePath(".", file)}");
        var content = await File.ReadAllTextAsync(file);
        var packageJson = JObject.Parse(content);
        var updated = false;

        using var jsonWriter = new StringWriter();
        using var writer = new JsonTextWriter(jsonWriter)
        {
            Formatting = Formatting.Indented
        };
        using var jsonReader = new JsonTextReader(new StringReader(content));

        while (jsonReader.Read())
        {
            if (jsonReader.TokenType == JsonToken.PropertyName)
            {
                var propertyName = jsonReader.Value?.ToString();
                writer.WriteToken(jsonReader.TokenType, jsonReader.Value);

                jsonReader.Read();
                if (propertyName == "version" || (propertyName?.StartsWith("com.reown.") ?? false))
                {
                    if (jsonReader.Value?.ToString() != newVersion)
                    {
                        writer.WriteValue(newVersion);
                        updated = true;
                        continue;
                    }
                }
                writer.WriteToken(jsonReader.TokenType, jsonReader.Value);
            }
            else
            {
                writer.WriteToken(jsonReader.TokenType, jsonReader.Value);
            }
        }

        if (updated)
        {
            await File.WriteAllTextAsync(file, jsonWriter.ToString());
            Log.Success($"Updated versions in {file}");
        }
    }
}

async Task UpdatePackagesLockVersionsAsync(string basePath, string newVersion)
{
    var files = Directory.EnumerateFiles(basePath, "packages-lock.json", SearchOption.AllDirectories).ToList();
    if (!files.Any())
    {
        Log.Warning($"No packages-lock.json files found in {Path.GetRelativePath(".", basePath)}");
        return;
    }

    foreach (var file in files)
    {
        Log.Info($"Processing: {Path.GetRelativePath(".", file)}");
        var content = await File.ReadAllTextAsync(file);
        var packageJson = JObject.Parse(content);
        var updated = false;
        var updatedPackages = new List<(string package, string from, string to)>();

        // Update versions in dependencies
        if (packageJson.TryGetValue("dependencies", out var dependencies) && dependencies is JObject depsObj)
        {
            foreach (var dep in depsObj.Properties())
            {
                if (dep.Name.StartsWith("com.reown."))
                {
                    var depObj = dep.Value as JObject;
                    if (depObj != null)
                    {
                        // Update dependencies of the package
                        if (depObj.TryGetValue("dependencies", out var nestedDeps) && nestedDeps is JObject nestedDepsObj)
                        {
                            foreach (var nestedDep in nestedDepsObj.Properties())
                            {
                                if (nestedDep.Name.StartsWith("com.reown."))
                                {
                                    var oldVersion = nestedDepsObj[nestedDep.Name].ToString();
                                    if (oldVersion != newVersion)
                                    {
                                        nestedDepsObj[nestedDep.Name] = newVersion;
                                        updated = true;
                                        updatedPackages.Add((nestedDep.Name, oldVersion, newVersion));
                                    }
                                }
                            }
                        }

                        // Update the package version itself if it's not a file reference
                        if (depObj.TryGetValue("version", out var versionToken))
                        {
                            var currentVersion = versionToken.ToString();
                            if (!currentVersion.StartsWith("file:") && currentVersion != newVersion)
                            {
                                depObj["version"] = newVersion;
                                updated = true;
                                updatedPackages.Add((dep.Name, currentVersion, newVersion));
                            }
                        }
                    }
                }
            }
        }

        if (updated)
        {
            await File.WriteAllTextAsync(file, packageJson.ToString(Formatting.Indented));
            Log.Success($"Updated {updatedPackages.Count} package versions:");
            foreach (var (package, from, to) in updatedPackages)
            {
                Log.Info($"  {package}: {from} → {to}");
            }
        }
    }
}
