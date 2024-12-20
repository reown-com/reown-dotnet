#!/usr/bin/env dotnet-script

#load "get-version.csx"

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
Console.WriteLine($"Looking for files in: {srcPath}");

var newVersion = GetVersion();
Console.WriteLine($"Current version: {newVersion}");

// Update version fields in the code
var versionFields = await FindVersionFieldsAsync(srcPath);
foreach (var field in versionFields)
{
    Console.WriteLine($"Found version field: {field.fieldName} = {field.version} in {field.filePath}");
    await UpdateVersionAsync(field.filePath, field.fieldName, newVersion);
}

// Update Unity sample app version
var projectSettingsPath = Path.GetFullPath("sample/Reown.AppKit.Unity/ProjectSettings/ProjectSettings.asset");
await UpdateProjectSettingsVersionAsync(projectSettingsPath, newVersion);

// Update Unity packages
await UpdatePackageVersionsAsync(srcPath, newVersion);

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
        Console.WriteLine($"Warning: ProjectSettings.asset not found at {filePath}");
        return;
    }

    var content = await File.ReadAllTextAsync(filePath);

    var pattern = @"(bundleVersion:)\s*(.+)";
    var replacement = $"$1 {newVersion}";

    var updatedContent = Regex.Replace(content, pattern, replacement);

    await File.WriteAllTextAsync(filePath, updatedContent);
    Console.WriteLine($"Updated bundleVersion in ProjectSettings.asset to {newVersion}");
}

async Task UpdatePackageVersionsAsync(string srcPath, string newVersion)
{
    foreach (var file in Directory.EnumerateFiles(srcPath, "package.json", SearchOption.AllDirectories))
    {
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
            Console.WriteLine($"Updated versions in {file}");
        }
    }
}
