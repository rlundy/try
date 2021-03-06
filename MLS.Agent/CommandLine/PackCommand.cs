// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.CommandLine;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using MLS.Agent.Tools;
using WorkspaceServer;
using WorkspaceServer.Packaging;

namespace MLS.Agent.CommandLine
{
    public static class PackCommand
    {
        public static async Task<string> Do(PackOptions options, IConsole console)
        {
            console.Out.WriteLine($"Creating package-tool from {options.PackTarget.FullName}");

            using (var disposableDirectory = DisposableDirectory.Create())
            {
                var temp = disposableDirectory.Directory;
                var temp_projects = temp.CreateSubdirectory("projects");

                var name = options.PackageName;

                var temp_projects_packtarget = temp_projects.CreateSubdirectory("packTarget");
                options.PackTarget.CopyTo(temp_projects_packtarget);

                if (options.EnableBlazor)
                {
                    string runnerDirectoryName = $"runner-{name}";
                    var temp_projects_blazorRunner = temp_projects.CreateSubdirectory(runnerDirectoryName);
                    var temp_projects_blazorRunner_mlsblazor = temp_projects_blazorRunner.CreateSubdirectory("MLS.Blazor");
                    await AddBlazorProject(temp_projects_blazorRunner_mlsblazor, GetProjectFile(temp_projects_packtarget), name);
                }

                var temp_toolproject = temp.CreateSubdirectory("project");
                var archivePath = Path.Combine(temp_toolproject.FullName, "package.zip");
                ZipFile.CreateFromDirectory(temp_projects.FullName, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);

                console.Out.WriteLine(archivePath);

                var projectFilePath = Path.Combine(temp_toolproject.FullName, "package-tool.csproj");
                var contentFilePath = Path.Combine(temp_toolproject.FullName, "program.cs");

                await File.WriteAllTextAsync(
                    projectFilePath,
                    typeof(Program).ReadManifestResource("MLS.Agent.MLS.PackageTool.csproj"));

                await File.WriteAllTextAsync(contentFilePath, typeof(Program).ReadManifestResource("MLS.Agent.Program.cs"));

                var dotnet = new Dotnet(temp_toolproject);
                var result = await dotnet.Build();

                result.ThrowOnFailure("Failed to build intermediate project.");
                var versionArg = "";

                if(!string.IsNullOrEmpty(options.Version))
                {
                    versionArg = $"/p:PackageVersion={options.Version}";
                }

                result = await dotnet.Pack($"/p:PackageId={name} /p:ToolCommandName={name} {versionArg} {projectFilePath} -o {options.OutputDirectory.FullName}");

                result.ThrowOnFailure("Package build failed.");

                return name;
            }
        }

        private static async Task AddBlazorProject(DirectoryInfo blazorTargetDirectory, FileInfo projectToReference, string name)
        {
            var initializer = new BlazorPackageInitializer(name, new System.Collections.Generic.List<string>());
            await initializer.Initialize(blazorTargetDirectory);

            await AddReference(blazorTargetDirectory, projectToReference);
        }

        private static async Task AddReference(DirectoryInfo blazorTargetDirectory, FileInfo projectToReference)
        {
            var dotnet = new Dotnet(blazorTargetDirectory);
            (await dotnet.AddReference(projectToReference)).ThrowOnFailure();
        }

        private static FileInfo GetProjectFile(DirectoryInfo directory)
        {
            return directory.GetFiles("*.csproj").Single();
        }
    }
}
