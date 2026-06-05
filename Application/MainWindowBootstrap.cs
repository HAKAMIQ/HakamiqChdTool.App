using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Licensing;
using HakamiqChdTool.App.Services.PostProcessing;
using HakamiqChdTool.App.Services.WpfShell;
using Serilog;
using System;
using System.Windows;

namespace HakamiqChdTool.App;

internal sealed class MainWindowBootstrap
{
    public MainWindowBootstrap(
        AppSettingsService settingsService,
        AppSettings settings,
        AppMetadata appMetadata,
        RuntimeToolService runtimeTools,
        ChdmanPathResolver chdmanPathResolver,
        IChdWorkflowOrchestrator workflowOrchestrator,
        IExternalLinkService externalLinkService,
        PostConversionArtifactService postConversionArtifacts,
        ILicenseService licenseService,
        IFeatureAccessService featureAccessService,
        OrphanedWorkItemScanner orphanedScanner,
        OrphanedWorkItemCleanupService orphanedCleanup,
        IWindowActivationService windowActivationService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appMetadata);
        ArgumentNullException.ThrowIfNull(runtimeTools);
        ArgumentNullException.ThrowIfNull(chdmanPathResolver);
        ArgumentNullException.ThrowIfNull(workflowOrchestrator);
        ArgumentNullException.ThrowIfNull(externalLinkService);
        ArgumentNullException.ThrowIfNull(postConversionArtifacts);
        ArgumentNullException.ThrowIfNull(licenseService);
        ArgumentNullException.ThrowIfNull(featureAccessService);
        ArgumentNullException.ThrowIfNull(orphanedScanner);
        ArgumentNullException.ThrowIfNull(orphanedCleanup);
        ArgumentNullException.ThrowIfNull(windowActivationService);

        SettingsService = settingsService;
        Settings = settings;
        AppMetadata = appMetadata;
        RuntimeTools = runtimeTools;
        ChdmanPathResolver = chdmanPathResolver;
        WorkflowOrchestrator = workflowOrchestrator;
        ExternalLinkService = externalLinkService;
        PostConversionArtifacts = postConversionArtifacts;
        LicenseService = licenseService;
        FeatureAccessService = featureAccessService;
        OrphanedScanner = orphanedScanner;
        OrphanedCleanup = orphanedCleanup;
        WindowActivationService = windowActivationService;
    }

    public AppSettingsService SettingsService { get; }

    public AppSettings Settings { get; }

    public AppMetadata AppMetadata { get; }

    public RuntimeToolService RuntimeTools { get; }

    public ChdmanPathResolver ChdmanPathResolver { get; }

    public IChdWorkflowOrchestrator WorkflowOrchestrator { get; }

    public IExternalLinkService ExternalLinkService { get; }

    public PostConversionArtifactService PostConversionArtifacts { get; }

    public ILicenseService LicenseService { get; }

    public IFeatureAccessService FeatureAccessService { get; }

    public OrphanedWorkItemScanner OrphanedScanner { get; }

    public OrphanedWorkItemCleanupService OrphanedCleanup { get; }

    public IWindowActivationService WindowActivationService { get; }

    public static MainWindowBootstrap ResolveForCurrentApplication()
    {
        if (Application.Current is App app)
        {
            return app.CreateMainWindowBootstrap();
        }

        return CreateDefault(
            new AppSettingsService(),
            AppSettings.CreateSafeDefaults(),
            AppMetadata.CreateDefault(),
            RuntimeToolService.Instance);
    }

    public static MainWindowBootstrap CreateDefault(
        AppSettingsService settingsService,
        AppSettings settings,
        AppMetadata appMetadata,
        RuntimeToolService runtimeTools)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appMetadata);
        ArgumentNullException.ThrowIfNull(runtimeTools);

        ChdmanPathResolver chdmanPathResolver = new(runtimeTools);
        PostConversionArtifactService postConversionArtifacts = new();
        IChdWorkflowOrchestrator workflowOrchestrator = CreateWorkflowOrchestrator(postConversionArtifacts);
        IExternalLinkService externalLinkService = new ExternalLinkService();
        ILicenseService licenseService = new LicenseService();
        IFeatureAccessService featureAccessService = new FeatureAccessService(licenseService);
        OrphanedWorkItemScanner orphanedScanner = new(settings);
        OrphanedWorkItemCleanupService orphanedCleanup = new(settings);
        IWindowActivationService windowActivationService = new WpfWindowActivationService();

        return new MainWindowBootstrap(
            settingsService,
            settings,
            appMetadata,
            runtimeTools,
            chdmanPathResolver,
            workflowOrchestrator,
            externalLinkService,
            postConversionArtifacts,
            licenseService,
            featureAccessService,
            orphanedScanner,
            orphanedCleanup,
            windowActivationService);
    }

    private static IChdWorkflowOrchestrator CreateWorkflowOrchestrator(PostConversionArtifactService postConversionArtifacts)
    {
        ArgumentNullException.ThrowIfNull(postConversionArtifacts);

        return new ChdWorkflowOrchestrator(
            new ChdConversionService(),
            new ArchiveExtractionService(),
            new ChdVerificationService(),
            new ChdInfoService(),
            postConversionArtifacts,
            Log.ForContext<ChdWorkflowOrchestrator>());
    }
}
