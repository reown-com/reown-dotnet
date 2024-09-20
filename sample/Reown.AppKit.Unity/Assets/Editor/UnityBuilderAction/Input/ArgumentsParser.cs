using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityBuilderAction.Input
{
    public class ArgumentsParser
    {
        private static string EOL = Environment.NewLine;

        private static readonly HashSet<string> Secrets = new()
        {
            "androidKeystorePass",
            "androidKeyaliasName",
            "androidKeyaliasPass",
            "mixpanelToken",
            "sentryToken"
        };

        public static Dictionary<string, string> GetValidatedOptions()
        {
            Dictionary<string, string> validatedOptions;
            ParseCommandLineArguments(out validatedOptions);

            string projectPath;
            if (!validatedOptions.TryGetValue("projectPath", out projectPath))
            {
                Console.WriteLine("Missing argument -projectPath");
                EditorApplication.Exit(110);
            }

            string buildTarget;
            if (!validatedOptions.TryGetValue("buildTarget", out buildTarget))
            {
                Console.WriteLine("Missing argument -buildTarget");
                EditorApplication.Exit(120);
            }

            if (!Enum.IsDefined(typeof(BuildTarget), buildTarget))
            {
                Console.WriteLine($"{buildTarget} is not a defined {nameof(BuildTarget)}");
                EditorApplication.Exit(121);
            }

            string customBuildPath;
            if (!validatedOptions.TryGetValue("customBuildPath", out customBuildPath))
            {
                Console.WriteLine("Missing argument -customBuildPath");
                EditorApplication.Exit(130);
            }

            const string defaultCustomBuildName = "TestBuild";
            string customBuildName;
            if (!validatedOptions.TryGetValue("customBuildName", out customBuildName))
            {
                Console.WriteLine($"Missing argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }
            else if (customBuildName == "")
            {
                Console.WriteLine($"Invalid argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }

            return validatedOptions;
        }

        private static void ParseCommandLineArguments(out Dictionary<string, string> providedArguments)
        {
            providedArguments = new Dictionary<string, string>();
            var args = Environment.GetCommandLineArgs();

            Console.WriteLine(
                $"{EOL}" +
                $"###########################{EOL}" +
                $"#    Parsing settings     #{EOL}" +
                $"###########################{EOL}" +
                $"{EOL}"
            );

            // Extract flags with optional values
            for (int current = 0, next = 1; current < args.Length; current++, next++)
            {
                // Parse flag
                var isFlag = args[current].StartsWith("-");
                if (!isFlag) continue;
                var flag = args[current].TrimStart('-');

                // Parse optional value
                var flagHasValue = next < args.Length && !args[next].StartsWith("-");
                var value = flagHasValue ? args[next].TrimStart('-') : "";
                var secret = Secrets.Contains(flag);
                var displayValue = secret ? "*HIDDEN*" : "\"" + value + "\"";

                // Assign
                Console.WriteLine($"Found flag \"{flag}\" with value {displayValue}.");
                providedArguments.Add(flag, value);
            }
        }
    }
}