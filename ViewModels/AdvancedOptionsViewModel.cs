using CommunityToolkit.Mvvm.ComponentModel;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.RedumpCatalog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class AdvancedOptionsViewModel : ObservableValidator
{
    private string _theme = "Light";
    private string _uiLanguage = AppLanguageService.ArabicLanguageName;
    private string _customOutputRoot = string.Empty;
    private string _redumpDatXmlPath = string.Empty;
    private string _redumpSystemName = string.Empty;
    private string _redumpDatabaseDownloadUrl = string.Empty;
    private bool _useCustomOutputRoot;
    private bool _organizeByPlatform;
    private bool _organizeByRegion;
    private bool _includeSubfolders;
    private bool _useBundledChdman = true;
    private string _externalChdmanPath = string.Empty;
    private bool _portableMode;
    private bool _verifyAfterConversion;
    private bool _skipExistingOutput;
    private bool _copyMatchingSbi;
    private bool _enableAutoM3uGeneration;
    private bool _overwriteExistingM3uPlaylists;
    private bool _deleteTemporaryExtraction;
    private bool _deleteFailedOutput;
    private bool _deleteSourceAfterVerifiedConversion;
    private bool _deleteSourceAfterVerifiedExtraction;
    private bool _enableDeepIntegrityCheck;
    private bool _applyStandardNamingBasedOnHash;
    private bool _isDatabaseAvailable;
    private bool _enableRedumpAutoSync;
    private AppSettings? _appliedSnapshot;
    private string? _redumpLastSyncedUtc;
    private string _databaseStatusText = ArabicUi.Get("LocAdv_DatabaseStatusUnavailable");
    private RedumpCatalogChoiceOption? _selectedRedumpPlatformOption;
    private RedumpCatalogChoiceOption? _selectedRedumpArtifactOption;
    private AdvancedProcessorOption? _selectedProcessorOption;
    private AdvancedProcessorOption? _selectedConcurrentConversionOption;
    private AdvancedChoiceOption? _selectedPerformanceMode;
    private AdvancedChoiceOption? _selectedPriorityMode;
    private AdvancedChoiceOption? _selectedCompressionPreset;
    private AdvancedChoiceOption? _selectedHunkPreset;
    private AdvancedChoiceOption? _selectedIsoCreateOverride;
    private bool _useCustomPendingWorkspace;
    private bool _showStorageAdvisorDialog = true;
    private string _pendingWorkspaceCustomRoot = string.Empty;
    private bool _canUsePostProcessingAutomation = true;
    private bool _canUseRedumpDeepIntegrity = true;
    private bool _canUseRedumpDatabaseImport = true;
    private bool _canUseStandardNamingSuggestion = true;
    private bool _canUseStorageAdvisor = true;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "Light", "Dark", "Hakamiq" };

    public ObservableCollection<AdvancedProcessorOption> ProcessorOptions { get; } = new();

    public ObservableCollection<AdvancedProcessorOption> ConcurrentConversionOptions { get; } = new();

    public ObservableCollection<AdvancedChoiceOption> PerformanceModeOptions { get; } = new()
    {
        new("Safe", ArabicUi.Get("LocAdv_PerformanceModeSafeLabel"), ArabicUi.Get("LocAdv_PerformanceModeSafeDescription")),
        new("Balanced", ArabicUi.Get("LocAdv_PerformanceModeBalancedLabel"), ArabicUi.Get("LocAdv_PerformanceModeBalancedDescription")),
        new("Fast", ArabicUi.Get("LocAdv_PerformanceModeFastLabel"), ArabicUi.Get("LocAdv_PerformanceModeFastDescription")),
        new("Manual", ArabicUi.Get("LocAdv_PerformanceModeManualLabel"), ArabicUi.Get("LocAdv_PerformanceModeManualDescription"))
    };

    public ObservableCollection<AdvancedChoiceOption> PriorityModeOptions { get; } = new()
    {
        new("Quiet", ArabicUi.Get("LocAdv_ProcessPriorityQuietLabel"), ArabicUi.Get("LocAdv_ProcessPriorityQuietDescription")),
        new("Normal", ArabicUi.Get("LocAdv_ProcessPriorityNormalLabel"), ArabicUi.Get("LocAdv_ProcessPriorityNormalDescription"))
    };

    public ObservableCollection<RedumpCatalogChoiceOption> RedumpPlatformOptions { get; } = new();

    public ObservableCollection<RedumpCatalogChoiceOption> RedumpArtifactOptions { get; } = new();

    public ObservableCollection<AdvancedChoiceOption> CompressionPresetOptions { get; } = new()
    {
        new("default", ArabicUi.Get("LocAdv_CompressionPresetDefaultLabel"), ArabicUi.Get("LocAdv_CompressionPresetDefaultDescription")),
        new("fast", ArabicUi.Get("LocAdv_CompressionPresetFastLabel"), ArabicUi.Get("LocAdv_CompressionPresetFastDescription")),
        new("balanced", ArabicUi.Get("LocAdv_CompressionPresetBalancedLabel"), ArabicUi.Get("LocAdv_CompressionPresetBalancedDescription")),
        new("max", ArabicUi.Get("LocAdv_CompressionPresetMaxLabel"), ArabicUi.Get("LocAdv_CompressionPresetMaxDescription"))
    };

    public ObservableCollection<AdvancedChoiceOption> HunkPresetOptions { get; } = new()
    {
        new("default", ArabicUi.Get("LocAdv_HunkPresetDefaultLabel"), ArabicUi.Get("LocAdv_HunkPresetDefaultDescription")),
        new("small", ArabicUi.Get("LocAdv_HunkPresetSmallLabel"), ArabicUi.Get("LocAdv_HunkPresetSmallDescription")),
        new("balanced", ArabicUi.Get("LocAdv_HunkPresetBalancedLabel"), ArabicUi.Get("LocAdv_HunkPresetBalancedDescription")),
        new("large", ArabicUi.Get("LocAdv_HunkPresetLargeLabel"), ArabicUi.Get("LocAdv_HunkPresetLargeDescription"))
    };

    public ObservableCollection<AdvancedChoiceOption> IsoCreateOverrideOptions { get; } = new()
    {
        new("Auto", ArabicUi.Get("LocAdv_IsoCreateOverrideAutoLabel"), ArabicUi.Get("LocAdv_IsoCreateOverrideAutoDescription")),
        new("CreateCd", ArabicUi.Get("LocAdv_IsoCreateOverrideCreateCdLabel"), ArabicUi.Get("LocAdv_IsoCreateOverrideCreateCdDescription")),
        new("CreateDvd", ArabicUi.Get("LocAdv_IsoCreateOverrideCreateDvdLabel"), ArabicUi.Get("LocAdv_IsoCreateOverrideCreateDvdDescription"))
    };

    public AdvancedOptionsViewModel()
    {
        LoadProcessorChoices();
        LoadConcurrentConversionChoices();
        LoadRedumpCatalogChoices();

        _selectedProcessorOption = ProcessorOptions.FirstOrDefault();
        _selectedConcurrentConversionOption = ConcurrentConversionOptions.FirstOrDefault();
        _selectedPerformanceMode = PerformanceModeOptions.FirstOrDefault();
        _selectedPriorityMode = PriorityModeOptions.FirstOrDefault();
        _selectedCompressionPreset = CompressionPresetOptions.FirstOrDefault();
        _selectedHunkPreset = HunkPresetOptions.FirstOrDefault();
        _selectedIsoCreateOverride = IsoCreateOverrideOptions.FirstOrDefault();

        ErrorsChanged += (_, _) => NotifySaveStateChanged();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(CanSave) and not nameof(CanConfirm) and not nameof(HasPendingChanges))
            {
                NotifySaveStateChanged();
            }
        };
    }

    public string UiLanguage
    {
        get => _uiLanguage;
        set
        {
            string effective = AppLanguageService.NormalizeLanguageName(value);

            if (SetProperty(ref _uiLanguage, effective))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool UseCustomOutputRoot
    {
        get => _useCustomOutputRoot;
        set
        {
            if (SetProperty(ref _useCustomOutputRoot, value))
            {
                ValidateProperty(CustomOutputRootValidationProxy, nameof(CustomOutputRootValidationProxy));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string CustomOutputRoot
    {
        get => _customOutputRoot;
        set
        {
            if (SetProperty(ref _customOutputRoot, value))
            {
                ValidateProperty(CustomOutputRootValidationProxy, nameof(CustomOutputRootValidationProxy));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string RedumpDatXmlPath
    {
        get => _redumpDatXmlPath;
        set
        {
            if (SetProperty(ref _redumpDatXmlPath, value))
            {
                ValidateProperty(RedumpDatXmlPathValidationProxy, nameof(RedumpDatXmlPathValidationProxy));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string RedumpSystemName
    {
        get => _redumpSystemName;
        set => SetProperty(ref _redumpSystemName, value);
    }

    public bool OrganizeByPlatform
    {
        get => _organizeByPlatform;
        set => SetProperty(ref _organizeByPlatform, value);
    }

    public bool OrganizeByRegion
    {
        get => _organizeByRegion;
        set => SetProperty(ref _organizeByRegion, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public bool ShowStorageAdvisorDialog
    {
        get => _showStorageAdvisorDialog;
        set
        {
            bool normalized = CanUseStorageAdvisor && value;
            if (SetProperty(ref _showStorageAdvisorDialog, normalized))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool UseBundledChdman
    {
        get => _useBundledChdman;
        set
        {
            if (SetProperty(ref _useBundledChdman, value))
            {
                ValidateProperty(ExternalChdmanPathValidationProxy, nameof(ExternalChdmanPathValidationProxy));
                OnPropertyChanged(nameof(UseExternalChdman));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool UseExternalChdman
    {
        get => !UseBundledChdman;
        set => UseBundledChdman = !value;
    }

    public string ExternalChdmanPath
    {
        get => _externalChdmanPath;
        set
        {
            if (SetProperty(ref _externalChdmanPath, value))
            {
                ValidateProperty(ExternalChdmanPathValidationProxy, nameof(ExternalChdmanPathValidationProxy));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool PortableMode
    {
        get => _portableMode;
        set => SetProperty(ref _portableMode, value);
    }

    public bool VerifyAfterConversion
    {
        get => _verifyAfterConversion;
        set => SetProperty(ref _verifyAfterConversion, value);
    }

    public bool SkipExistingOutput
    {
        get => _skipExistingOutput;
        set => SetProperty(ref _skipExistingOutput, value);
    }

    public bool CopyMatchingSbi
    {
        get => _copyMatchingSbi;
        set => SetProperty(ref _copyMatchingSbi, value && CanUsePostProcessingAutomation);
    }

    public bool EnableAutoM3uGeneration
    {
        get => _enableAutoM3uGeneration;
        set
        {
            bool normalized = value && CanUsePostProcessingAutomation;
            if (SetProperty(ref _enableAutoM3uGeneration, normalized))
            {
                OnPropertyChanged(nameof(CanOverwriteExistingM3uPlaylists));
                if (!normalized)
                {
                    OverwriteExistingM3uPlaylists = false;
                }
            }
        }
    }

    public bool CanOverwriteExistingM3uPlaylists => CanUsePostProcessingAutomation && EnableAutoM3uGeneration;

    public bool OverwriteExistingM3uPlaylists
    {
        get => _overwriteExistingM3uPlaylists;
        set => SetProperty(ref _overwriteExistingM3uPlaylists, value && CanOverwriteExistingM3uPlaylists);
    }

    public bool DeleteTemporaryExtraction
    {
        get => _deleteTemporaryExtraction;
        set => SetProperty(ref _deleteTemporaryExtraction, value);
    }

    public bool DeleteFailedOutput
    {
        get => _deleteFailedOutput;
        set => SetProperty(ref _deleteFailedOutput, value);
    }

    public bool DeleteSourceAfterVerifiedConversion
    {
        get => _deleteSourceAfterVerifiedConversion;
        set => SetProperty(ref _deleteSourceAfterVerifiedConversion, value);
    }

    public bool DeleteSourceAfterVerifiedExtraction
    {
        get => _deleteSourceAfterVerifiedExtraction;
        set => SetProperty(ref _deleteSourceAfterVerifiedExtraction, value);
    }

    public bool CanEnableDeepIntegrityCheck => CanUseRedumpDeepIntegrity;

    public bool EnableDeepIntegrityCheck
    {
        get => _enableDeepIntegrityCheck;
        set
        {
            bool normalized = value && CanUseRedumpDeepIntegrity;
            if (SetProperty(ref _enableDeepIntegrityCheck, normalized))
            {
                OnPropertyChanged(nameof(CanEnableStandardNaming));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool ApplyStandardNamingBasedOnHash
    {
        get => _applyStandardNamingBasedOnHash;
        set
        {
            bool normalized = CanUseStandardNamingSuggestion && value;
            SetProperty(ref _applyStandardNamingBasedOnHash, normalized);
        }
    }

    public RedumpCatalogChoiceOption? SelectedRedumpPlatformOption
    {
        get => _selectedRedumpPlatformOption;
        set
        {
            if (SetProperty(ref _selectedRedumpPlatformOption, value))
            {
                RefreshRedumpArtifactOptions(value?.Key);
                UpdateRedumpDownloadUrlFromSelection(overwriteExistingCatalogUrl: true);
                OnPropertyChanged(nameof(SelectedRedumpPlatformDescription));
                OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public RedumpCatalogChoiceOption? SelectedRedumpArtifactOption
    {
        get => _selectedRedumpArtifactOption;
        set
        {
            if (SetProperty(ref _selectedRedumpArtifactOption, value))
            {
                UpdateRedumpDownloadUrlFromSelection(overwriteExistingCatalogUrl: true);
                OnPropertyChanged(nameof(SelectedRedumpArtifactDescription));
                OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string SelectedRedumpPlatformDescription => ResolveOptionDescription(SelectedRedumpPlatformOption);

    public string SelectedRedumpArtifactDescription => ResolveOptionDescription(SelectedRedumpArtifactOption);

    public bool CanDownloadSelectedRedumpDatabase => CanUseRedumpDatabaseImport && IsSafeDownloadUrl(RedumpDatabaseDownloadUrl);

    public string RedumpDatabaseDownloadUrl
    {
        get => _redumpDatabaseDownloadUrl;
        set
        {
            if (SetProperty(ref _redumpDatabaseDownloadUrl, value))
            {
                ValidateProperty(RedumpDatabaseDownloadUrlValidationProxy, nameof(RedumpDatabaseDownloadUrlValidationProxy));
                OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool EnableRedumpAutoSync
    {
        get => _enableRedumpAutoSync;
        set
        {
            bool normalized = CanUseRedumpDatabaseImport && value;
            if (SetProperty(ref _enableRedumpAutoSync, normalized))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsDatabaseAvailable
    {
        get => _isDatabaseAvailable;
        set
        {
            if (SetProperty(ref _isDatabaseAvailable, value))
            {
                OnPropertyChanged(nameof(CanEnableDeepIntegrityCheck));
                OnPropertyChanged(nameof(CanEnableStandardNaming));
                OnPropertyChanged(nameof(IntegrityFeatureOpacity));
                OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string DatabaseStatusText
    {
        get => _databaseStatusText;
        set => SetProperty(ref _databaseStatusText, value);
    }

    public string DatabaseLastSyncedDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_redumpLastSyncedUtc))
            {
                return ArabicUi.Get("LocAdv_DatabaseLastSyncedEmpty");
            }

            if (DateTimeOffset.TryParse(_redumpLastSyncedUtc, out DateTimeOffset parsed))
            {
                return ArabicUi.Format("LocAdv_DatabaseLastSyncedValue", parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            }

            return ArabicUi.Format("LocAdv_DatabaseLastSyncedValue", _redumpLastSyncedUtc);
        }
    }

    public double IntegrityFeatureOpacity => IsDatabaseAvailable ? 1.0 : 0.55;

    public bool CanEnableStandardNaming => CanUseStandardNamingSuggestion;

    public bool CanUsePostProcessingAutomation
    {
        get => _canUsePostProcessingAutomation;
        set
        {
            if (SetProperty(ref _canUsePostProcessingAutomation, value))
            {
                if (!value)
                {
                    CopyMatchingSbi = false;
                    EnableAutoM3uGeneration = false;
                    OverwriteExistingM3uPlaylists = false;
                }

                OnPropertyChanged(nameof(CanOverwriteExistingM3uPlaylists));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanUseRedumpDeepIntegrity
    {
        get => _canUseRedumpDeepIntegrity;
        set
        {
            if (SetProperty(ref _canUseRedumpDeepIntegrity, value))
            {
                if (!value)
                {
                    EnableDeepIntegrityCheck = false;
                }

                OnPropertyChanged(nameof(CanEnableDeepIntegrityCheck));
                OnPropertyChanged(nameof(CanEnableStandardNaming));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanUseRedumpDatabaseImport
    {
        get => _canUseRedumpDatabaseImport;
        set
        {
            if (SetProperty(ref _canUseRedumpDatabaseImport, value))
            {
                if (!value)
                {
                    EnableRedumpAutoSync = false;
                }

                OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanUseStandardNamingSuggestion
    {
        get => _canUseStandardNamingSuggestion;
        set
        {
            if (SetProperty(ref _canUseStandardNamingSuggestion, value))
            {
                if (!value)
                {
                    ApplyStandardNamingBasedOnHash = false;
                }

                OnPropertyChanged(nameof(CanEnableStandardNaming));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanUseStorageAdvisor
    {
        get => _canUseStorageAdvisor;
        set
        {
            if (SetProperty(ref _canUseStorageAdvisor, value))
            {
                if (!value)
                {
                    ShowStorageAdvisorDialog = false;
                }

                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool HasPendingChanges => _appliedSnapshot is not null && !CurrentValuesEqual(_appliedSnapshot);

    public bool CanConfirm => !HasErrors;

    public bool CanSave => CanConfirm && HasPendingChanges;

    public string ProcessorSummary => BuildProcessorSummary();

    public string ProcessorHint => ArabicUi.Get("LocAdv_ProcessorHint");

    public string ConcurrentConversionDescription => BuildConcurrentConversionDescription();

    public string PerformanceModeDescription => SelectedPerformanceMode?.Description ?? string.Empty;

    public string PriorityModeDescription => SelectedPriorityMode?.Description ?? string.Empty;

    public string CompressionPresetDescription => SelectedCompressionPreset?.Description ?? string.Empty;

    public string HunkPresetDescription => SelectedHunkPreset?.Description ?? string.Empty;

    public string IsoCreateOverrideDescription => SelectedIsoCreateOverride?.Description ?? string.Empty;

    public string ProcessorSelectionDescription
    {
        get
        {
            if (SelectedProcessorOption is null)
            {
                return string.Empty;
            }

            if (SelectedProcessorOption.Value == 0)
            {
                int effectiveProcessors = ProcessorTopologyService.ResolveDefaultAutoChdmanProcessorCount();
                int availableLogicalProcessors = ProcessorTopologyService.GetAvailableLogicalProcessorCount();

                return effectiveProcessors > 0
                    ? ArabicUi.Format("LocAdv_ProcessorSelectionAutoLimited", effectiveProcessors, availableLogicalProcessors)
                    : ArabicUi.Get("LocAdv_ProcessorSelectionAutoDefault");
            }

            return BuildManualProcessorSelectionDescription(SelectedProcessorOption.Value);
        }
    }

    private static string BuildManualProcessorSelectionDescription(int selectedProcessorCount)
    {
        int availableLogicalProcessors = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        int effectiveProcessorCount = Math.Clamp(selectedProcessorCount, 1, availableLogicalProcessors);
        double usageRatio = (double)effectiveProcessorCount / availableLogicalProcessors;

        string resourceKey = usageRatio switch
        {
            <= 0.25d => "LocAdv_ProcessorSelectionManualLow",
            <= 0.50d => "LocAdv_ProcessorSelectionManualBalanced",
            <= 0.75d => "LocAdv_ProcessorSelectionManualHigh",
            _ => "LocAdv_ProcessorSelectionManualMaximum"
        };

        return ArabicUi.Format(resourceKey, effectiveProcessorCount, availableLogicalProcessors);
    }

    public AdvancedProcessorOption? SelectedProcessorOption
    {
        get => _selectedProcessorOption;
        set
        {
            if (SetProperty(ref _selectedProcessorOption, value))
            {
                ValidateProperty(SelectedProcessorValueValidationProxy, nameof(SelectedProcessorValueValidationProxy));
                OnPropertyChanged(nameof(ProcessorSelectionDescription));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }


    public AdvancedProcessorOption? SelectedConcurrentConversionOption
    {
        get => _selectedConcurrentConversionOption;
        set
        {
            if (SetProperty(ref _selectedConcurrentConversionOption, value))
            {
                ValidateProperty(SelectedConcurrentConversionValueValidationProxy, nameof(SelectedConcurrentConversionValueValidationProxy));
                OnPropertyChanged(nameof(ConcurrentConversionDescription));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public AdvancedChoiceOption? SelectedPerformanceMode
    {
        get => _selectedPerformanceMode;
        set
        {
            if (SetProperty(ref _selectedPerformanceMode, value))
            {
                OnPropertyChanged(nameof(PerformanceModeDescription));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public AdvancedChoiceOption? SelectedPriorityMode
    {
        get => _selectedPriorityMode;
        set
        {
            if (SetProperty(ref _selectedPriorityMode, value))
            {
                OnPropertyChanged(nameof(PriorityModeDescription));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public AdvancedChoiceOption? SelectedCompressionPreset
    {
        get => _selectedCompressionPreset;
        set
        {
            if (SetProperty(ref _selectedCompressionPreset, value))
            {
                OnPropertyChanged(nameof(CompressionPresetDescription));
            }
        }
    }

    public AdvancedChoiceOption? SelectedHunkPreset
    {
        get => _selectedHunkPreset;
        set
        {
            if (SetProperty(ref _selectedHunkPreset, value))
            {
                OnPropertyChanged(nameof(HunkPresetDescription));
            }
        }
    }

    public AdvancedChoiceOption? SelectedIsoCreateOverride
    {
        get => _selectedIsoCreateOverride;
        set
        {
            if (SetProperty(ref _selectedIsoCreateOverride, value))
            {
                OnPropertyChanged(nameof(IsoCreateOverrideDescription));
            }
        }
    }

    public bool UseCustomPendingWorkspace
    {
        get => _useCustomPendingWorkspace;
        set
        {
            if (_useCustomPendingWorkspace == value)
            {
                return;
            }

            _useCustomPendingWorkspace = value;
            OnPropertyChanged(nameof(UseCustomPendingWorkspace));
            OnPropertyChanged(nameof(PendingWorkspaceModeDisplay));
            ValidateProperty(PendingWorkspaceCustomRootValidationProxy, nameof(PendingWorkspaceCustomRootValidationProxy));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public string PendingWorkspaceCustomRoot
    {
        get => _pendingWorkspaceCustomRoot;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(_pendingWorkspaceCustomRoot, next, StringComparison.Ordinal))
            {
                return;
            }

            _pendingWorkspaceCustomRoot = next;
            OnPropertyChanged(nameof(PendingWorkspaceCustomRoot));
            ValidateProperty(PendingWorkspaceCustomRootValidationProxy, nameof(PendingWorkspaceCustomRootValidationProxy));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public string PendingWorkspaceModeDisplay =>
        UseCustomPendingWorkspace
            ? ArabicUi.Get("LocAdv_PendingWorkspaceModeCustom")
            : ArabicUi.Get("LocAdv_PendingWorkspaceModeAutomatic");

    [CustomValidation(typeof(AdvancedOptionsViewModel), nameof(ValidateCustomOutputRoot))]
    public string CustomOutputRootValidationProxy => CustomOutputRoot;

    [CustomValidation(typeof(AdvancedOptionsViewModel), nameof(ValidateRedumpDatXmlPath))]
    public string RedumpDatXmlPathValidationProxy => RedumpDatXmlPath;

    [CustomValidation(typeof(AdvancedOptionsViewModel), nameof(ValidateRedumpDatabaseDownloadUrl))]
    public string RedumpDatabaseDownloadUrlValidationProxy => RedumpDatabaseDownloadUrl;

    [CustomValidation(typeof(AdvancedOptionsViewModel), nameof(ValidateExternalChdmanPath))]
    public string ExternalChdmanPathValidationProxy => ExternalChdmanPath;

    [CustomValidation(typeof(AdvancedOptionsViewModel), nameof(ValidateSelectedProcessorValue))]
    public int SelectedProcessorValueValidationProxy => SelectedProcessorOption?.Value ?? 0;

    [CustomValidation(typeof(AdvancedOptionsViewModel), nameof(ValidateSelectedConcurrentConversionValue))]
    public int SelectedConcurrentConversionValueValidationProxy => SelectedConcurrentConversionOption?.Value ?? AppSettings.DefaultMaxConcurrentConversions;

    private void NotifySaveStateChanged()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanSave));
    }

    private bool CurrentValuesEqual(AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string redumpPlatformMode = SelectedRedumpPlatformOption is null
            ? "Auto"
            : string.Equals(SelectedRedumpPlatformOption.Key, "Auto", StringComparison.OrdinalIgnoreCase)
                ? "Auto"
                : string.Equals(SelectedRedumpPlatformOption.Key, "Unknown", StringComparison.OrdinalIgnoreCase)
                    ? "Unknown"
                    : "Platform";

        string redumpPlatformKey = redumpPlatformMode == "Platform"
            ? SelectedRedumpPlatformOption?.Key ?? string.Empty
            : string.Empty;

        string compressionCodecs = SelectedCompressionPreset?.Key switch
        {
            "fast" => "preset:fast",
            "balanced" => "preset:balanced",
            "max" => "preset:max",
            _ => "preset:default"
        };

        int hunkSizeBytes = SelectedHunkPreset?.Key switch
        {
            "small" => -1,
            "balanced" => -2,
            "large" => -3,
            _ => 0
        };

        IsoCreateCommandOverride isoCreateOverride = Enum.TryParse<IsoCreateCommandOverride>(
            SelectedIsoCreateOverride?.Key,
            true,
            out var iso)
                ? iso
                : IsoCreateCommandOverride.Auto;

        ConversionPerformanceMode performanceMode = Enum.TryParse<ConversionPerformanceMode>(
            SelectedPerformanceMode?.Key,
            true,
            out var performance)
                ? performance
                : ConversionPerformanceMode.Safe;

        ChdmanProcessPriorityMode priorityMode = Enum.TryParse<ChdmanProcessPriorityMode>(
            SelectedPriorityMode?.Key,
            true,
            out var priority)
                ? priority
                : ChdmanProcessPriorityMode.Quiet;

        bool useCustomPendingWorkspace = UseCustomPendingWorkspace;
        PendingWorkspaceMode pendingWorkspaceMode = useCustomPendingWorkspace
            ? PendingWorkspaceMode.Custom
            : PendingWorkspaceMode.Automatic;

        return StringEquals(Theme, snapshot.Theme)
            && StringEquals(AppLanguageService.NormalizeLanguageName(UiLanguage), AppLanguageService.NormalizeLanguageName(snapshot.UiLanguage))
            && UseCustomOutputRoot == snapshot.UseCustomOutputRoot
            && StringEquals(CustomOutputRoot.Trim(), snapshot.CustomOutputRoot)
            && pendingWorkspaceMode == snapshot.PendingWorkspaceMode
            && StringEquals(PendingWorkspaceCustomRoot.Trim(), snapshot.PendingWorkspaceCustomRoot)
            && useCustomPendingWorkspace == snapshot.UseCustomPendingWorkspace
            && StringEquals(RedumpDatXmlPath.Trim(), snapshot.RedumpDatXmlPath)
            && StringEquals(RedumpSystemName.Trim(), snapshot.RedumpSystemName)
            && StringEquals(redumpPlatformMode, snapshot.RedumpPlatformMode)
            && StringEquals(redumpPlatformKey, snapshot.RedumpPlatformKey)
            && StringEquals(SelectedRedumpArtifactOption?.Key ?? "Datfile", snapshot.RedumpArtifactKind)
            && OrganizeByPlatform == snapshot.OrganizeByPlatform
            && OrganizeByRegion == snapshot.OrganizeByRegion
            && IncludeSubfolders == snapshot.IncludeSubfolders
            && ShowStorageAdvisorDialog == !snapshot.SuppressStorageAdvisorDialog
            && UseBundledChdman == snapshot.UseBundledChdman
            && StringEquals(ExternalChdmanPath.Trim(), snapshot.ExternalChdmanPath)
            && PortableMode == snapshot.PortableMode
            && VerifyAfterConversion == snapshot.VerifyAfterConversion
            && SkipExistingOutput == snapshot.SkipExistingOutput
            && CopyMatchingSbi == snapshot.CopyMatchingSbi
            && EnableAutoM3uGeneration == snapshot.EnableAutoM3uGeneration
            && OverwriteExistingM3uPlaylists == snapshot.OverwriteExistingM3uPlaylists
            && DeleteTemporaryExtraction == snapshot.DeleteTemporaryExtraction
            && DeleteFailedOutput == snapshot.DeleteFailedOutput
            && DeleteSourceAfterVerifiedConversion == snapshot.DeleteSourceAfterVerifiedConversion
            && DeleteSourceAfterVerifiedExtraction == snapshot.DeleteSourceAfterVerifiedExtraction
            && EnableDeepIntegrityCheck == snapshot.EnableDeepIntegrityCheck
            && ApplyStandardNamingBasedOnHash == snapshot.ApplyStandardNamingBasedOnHash
            && StringEquals(RedumpDatabaseDownloadUrl.Trim(), snapshot.RedumpDatabaseDownloadUrl)
            && EnableRedumpAutoSync == snapshot.EnableRedumpAutoSync
            && StringEquals(_redumpLastSyncedUtc ?? string.Empty, snapshot.RedumpLastSyncedUtc)
            && (SelectedProcessorOption?.Value ?? 0) == snapshot.MaxProcessorCount
            && (SelectedConcurrentConversionOption?.Value ?? AppSettings.DefaultMaxConcurrentConversions) == AppSettings.NormalizeMaxConcurrentConversions(snapshot.MaxConcurrentConversions)
            && performanceMode == snapshot.PerformanceMode
            && priorityMode == snapshot.ChdmanPriorityMode
            && StringEquals(compressionCodecs, snapshot.CompressionCodecs)
            && hunkSizeBytes == snapshot.HunkSizeBytes
            && isoCreateOverride == snapshot.IsoCreateCommandOverride;
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
    }

    private void LoadRedumpCatalogChoices()
    {
        RedumpPlatformOptions.Clear();

        foreach (RedumpCatalogOption option in RedumpSystemCatalog.BuildPlatformOptions())
        {
            RedumpPlatformOptions.Add(RedumpCatalogChoiceOption.FromCatalogOption(option));
        }

        SelectedRedumpPlatformOption = RedumpPlatformOptions.FirstOrDefault();
    }

    private void RefreshRedumpArtifactOptions(string? platformKey)
    {
        RedumpArtifactOptions.Clear();

        foreach (RedumpCatalogOption option in RedumpSystemCatalog.BuildArtifactOptions(platformKey))
        {
            RedumpArtifactOptions.Add(RedumpCatalogChoiceOption.FromCatalogOption(option));
        }

        _selectedRedumpArtifactOption = RedumpArtifactOptions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRedumpArtifactOption));
        OnPropertyChanged(nameof(SelectedRedumpArtifactDescription));
    }

    private void UpdateRedumpDownloadUrlFromSelection(bool overwriteExistingCatalogUrl)
    {
        string selectedUrl = SelectedRedumpArtifactOption?.Url ?? string.Empty;

        if (overwriteExistingCatalogUrl || string.IsNullOrWhiteSpace(RedumpDatabaseDownloadUrl))
        {
            RedumpDatabaseDownloadUrl = selectedUrl;
            return;
        }

        OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
    }

    private RedumpCatalogChoiceOption ResolveRedumpPlatformOption(string? mode, string? platformKey)
    {
        string key = string.Equals(mode, "Platform", StringComparison.OrdinalIgnoreCase)
            ? platformKey?.Trim() ?? string.Empty
            : string.IsNullOrWhiteSpace(mode)
                ? "Auto"
                : mode.Trim();

        return RedumpPlatformOptions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? RedumpPlatformOptions.FirstOrDefault()
            ?? new RedumpCatalogChoiceOption(
                "Auto",
                "LocRedumpCatalog_Platform_Auto_Label",
                "LocRedumpCatalog_Platform_Auto_Description");
    }

    private RedumpCatalogChoiceOption ResolveRedumpArtifactOption(string? artifactKey)
    {
        return RedumpArtifactOptions.FirstOrDefault(x => string.Equals(x.Key, artifactKey?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? RedumpArtifactOptions.FirstOrDefault()
            ?? new RedumpCatalogChoiceOption(
                "Datfile",
                "LocRedumpCatalog_Artifact_Datfile_Label",
                "LocRedumpCatalog_Artifact_Datfile_Description");
    }

    private AdvancedChoiceOption? ResolveIsoCreateOverride(IsoCreateCommandOverride value) =>
        IsoCreateOverrideOptions.FirstOrDefault(x => string.Equals(x.Key, value.ToString(), StringComparison.OrdinalIgnoreCase))
        ?? IsoCreateOverrideOptions.FirstOrDefault();

    private static string ResolveOptionDescription(RedumpCatalogChoiceOption? option)
    {
        return option?.Description ?? string.Empty;
    }

    private static bool IsSafeDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
               && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }
}
