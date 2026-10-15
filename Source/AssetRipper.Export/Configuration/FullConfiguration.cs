using AssetRipper.Configuration;
using AssetRipper.Export.UnityProjects.Configuration;
using AssetRipper.Import.Configuration;
using AssetRipper.Mining.PredefinedAssets;
using AssetRipper.Processing.Configuration;

namespace AssetRipper.Export.Configuration;

public class FullConfiguration : CoreConfiguration
{
	public ProcessingSettings ProcessingSettings
	{
		get => SingletonData.GetStoredValue<ProcessingSettings>(nameof(ProcessingSettings));
		set => SingletonData.SetStoredValue(nameof(ProcessingSettings), value);
	}

	public ExportSettings ExportSettings
	{
		get => SingletonData.GetStoredValue<ExportSettings>(nameof(ExportSettings));
		set => SingletonData.SetStoredValue(nameof(ExportSettings), value);
	}

	/// <summary>
	/// Restricts which assets are written during export. Empty by default (everything is exported).
	/// </summary>
	public ExportFilter ExportFilter
	{
		get => SingletonData.GetStoredValue<ExportFilter>(nameof(ExportFilter));
		set => SingletonData.SetStoredValue(nameof(ExportFilter), value);
	}

	/// <summary>
	/// User supplied export destinations for individual assets. Empty by default.
	/// </summary>
	public AssetPathOverrides AssetPathOverrides
	{
		get => SingletonData.GetStoredValue<AssetPathOverrides>(nameof(AssetPathOverrides));
		set => SingletonData.SetStoredValue(nameof(AssetPathOverrides), value);
	}

	/// <summary>
	/// Packages whose scripts and assets are referenced instead of exported (User-Defined Package Export). Empty by default.
	/// </summary>
	public PackageMappings PackageMappings
	{
		get => SingletonData.GetStoredValue<PackageMappings>(nameof(PackageMappings));
		set => SingletonData.SetStoredValue(nameof(PackageMappings), value);
	}

	public bool SaveSettingsToDisk => ExportSettings.SaveSettingsToDisk;

	public string? LanguageCode
	{
		get => ExportSettings.LanguageCode;
		set => ExportSettings.LanguageCode = value;
	}

	public FullConfiguration()
	{
		SingletonData.Add(nameof(ProcessingSettings), new JsonDataInstance<ProcessingSettings>(SerializedSettingsContext.Default.ProcessingSettings));
		SingletonData.Add(nameof(ExportSettings), new JsonDataInstance<ExportSettings>(SerializedSettingsContext.Default.ExportSettings));
		SingletonData.Add(nameof(EngineResourceData), new JsonDataInstance<EngineResourceData?>(EngineResourceDataContext.Default.NullableEngineResourceData));
		SingletonData.Add(nameof(ExportFilter), new JsonDataInstance<ExportFilter>(ExportFilterContext.Default.ExportFilter));
		SingletonData.Add(nameof(AssetPathOverrides), new JsonDataInstance<AssetPathOverrides>(AssetPathOverridesContext.Default.AssetPathOverrides));
		SingletonData.Add(nameof(PackageMappings), new JsonDataInstance<PackageMappings>(PackageMappingsContext.Default.PackageMappings));
	}

	public override void LogConfigurationValues()
	{
		base.LogConfigurationValues();
		ProcessingSettings.Log();
		ExportSettings.Log();
	}

	public void LoadFromDefaultPath()
	{
		if (SerializedSettings.TryLoadFromDefaultPath(out SerializedSettings settings))
		{
			ImportSettings = settings.Import;
			ProcessingSettings = settings.Processing;
			ExportSettings = settings.Export;
		}
	}

	public void SaveToDefaultPath()
	{
		new SerializedSettings(ImportSettings, ProcessingSettings, ExportSettings).SaveToDefaultPath();
	}

	public void MaybeSaveToDefaultPath()
	{
		if (SaveSettingsToDisk)
		{
			SaveToDefaultPath();
		}
	}
}
