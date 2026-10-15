using AssetRipper.Export.Configuration;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Project;

public class PackageManifestPostExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		string packagesDirectory = fileSystem.Path.Join(settings.ProjectRootPath, "Packages");
		fileSystem.Directory.Create(packagesDirectory);
		string path = fileSystem.Path.Join(packagesDirectory, "manifest.json");
		using Stream stream = fileSystem.File.Create(path);
		PackageManifest manifest = CreateManifest(settings.Version);
		foreach (PackageDefinition package in settings.PackageMappings.Packages)
		{
			if (!string.IsNullOrWhiteSpace(package.Name) && !string.IsNullOrWhiteSpace(package.Version))
			{
				manifest.Dependencies[package.Name] = package.Version;
			}
		}
		manifest.Save(stream);
	}

	protected virtual PackageManifest CreateManifest(UnityVersion version)
	{
		return PackageManifest.CreateDefault(version);
	}
}
