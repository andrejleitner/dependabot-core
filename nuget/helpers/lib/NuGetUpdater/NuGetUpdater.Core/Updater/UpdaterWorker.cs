using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NuGetUpdater.Core;

public class UpdaterWorker
{
    private readonly Logger _logger;
    private readonly HashSet<string> _processedGlobalJsonPaths = new(StringComparer.OrdinalIgnoreCase);

    public UpdaterWorker(Logger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(string repoRootPath, string workspacePath, string dependencyName, string previousDependencyVersion, string newDependencyVersion, bool isTransitive)
    {
        MSBuildHelper.RegisterMSBuild();

        if (!Path.IsPathRooted(workspacePath) || !File.Exists(workspacePath))
        {
            workspacePath = Path.GetFullPath(Path.Join(repoRootPath, workspacePath));
        }

        if (!isTransitive)
        {
            await DotNetToolsJsonUpdater.UpdateDependencyAsync(repoRootPath, workspacePath, dependencyName, previousDependencyVersion, newDependencyVersion, _logger);
        }

        var extension = Path.GetExtension(workspacePath).ToLowerInvariant();
        switch (extension)
        {
            case ".sln":
                await RunForSolutionAsync(repoRootPath, workspacePath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive);
                break;
            case ".proj":
                await RunForProjFileAsync(repoRootPath, workspacePath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive);
                break;
            case ".csproj":
            case ".fsproj":
            case ".vbproj":
                await RunForProjectAsync(repoRootPath, workspacePath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive);
                break;
            default:
                _logger.Log($"File extension [{extension}] is not supported.");
                break;
        }

        _processedGlobalJsonPaths.Clear();
    }

    private async Task RunForSolutionAsync(
        string repoRootPath,
        string solutionPath,
        string dependencyName,
        string previousDependencyVersion,
        string newDependencyVersion,
        bool isTransitive)
    {
        _logger.Log($"Running for solution [{Path.GetRelativePath(repoRootPath, solutionPath)}]");
        var projectPaths = MSBuildHelper.GetProjectPathsFromSolution(solutionPath);
        foreach (var projectPath in projectPaths)
        {
            await RunForProjectAsync(repoRootPath, projectPath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive);
        }
    }

    private async Task RunForProjFileAsync(
        string repoRootPath,
        string projFilePath,
        string dependencyName,
        string previousDependencyVersion,
        string newDependencyVersion,
        bool isTransitive)
    {
        _logger.Log($"Running for proj file [{Path.GetRelativePath(repoRootPath, projFilePath)}]");
        if (!File.Exists(projFilePath))
        {
            _logger.Log($"File [{projFilePath}] does not exist.");
            return;
        }

        var projectFilePaths = MSBuildHelper.GetProjectPathsFromProject(projFilePath);
        foreach (var projectFullPath in projectFilePaths)
        {
            // If there is some MSBuild logic that needs to run to fully resolve the path skip the project
            if (File.Exists(projectFullPath))
            {
                await RunForProjectAsync(repoRootPath, projectFullPath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive);
            }
        }
    }

    private async Task RunForProjectAsync(
        string repoRootPath,
        string projectPath,
        string dependencyName,
        string previousDependencyVersion,
        string newDependencyVersion,
        bool isTransitive)
    {
        _logger.Log($"Running for project [{projectPath}]");

        if (!isTransitive
            && MSBuildHelper.GetGlobalJsonPath(repoRootPath, projectPath) is { } globalJsonPath
            && !_processedGlobalJsonPaths.Contains(globalJsonPath))
        {
            _processedGlobalJsonPaths.Add(globalJsonPath);
            await GlobalJsonUpdater.UpdateDependencyAsync(repoRootPath, globalJsonPath, dependencyName, previousDependencyVersion, newDependencyVersion, _logger);
        }

        if (NuGetHelper.HasProjectConfigFile(projectPath))
        {
            await PackagesConfigUpdater.UpdateDependencyAsync(repoRootPath, projectPath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive, _logger);
        }

        // Some repos use a mix of packages.config and PackageReference
        await SdkPackageUpdater.UpdateDependencyAsync(repoRootPath, projectPath, dependencyName, previousDependencyVersion, newDependencyVersion, isTransitive, _logger);

        _logger.Log("Update complete.");
    }
}
