using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.PostProcessing;
using HakamiqChdTool.App.Ui.Shell;
using Serilog;
using System;
using System.Windows;

namespace HakamiqChdTool.App.Startup;

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
        IAppFeatureService appFeatureService,
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
        ArgumentNullException.ThrowIfNull(appFeatureService);
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
        AppFeatureService = appFeatureService;
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


    public IAppFeatureService AppFeatureService { get; }

    public OrphanedWorkItemScanner OrphanedScanner { get; }

    public OrphanedWorkItemCleanupService OrphanedCleanup { get; }

    public IWindowActivationService WindowActivationService { get; }

    public static MainWindowBootstrap ResolveForCurrentApplication()
    {
        if (System.Windows.Application.Current is App app)
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
        IAppFeatureService appFeatureService = new AppFeatureService();
        OrphanedWorkItemScanner orphanedScanner = new(settings);
        OrphanedWorkItemCleanupService orphanedCleanup = new(settings);
        IWindowActivationService windowActivationService = new WindowActivator();

        return new MainWindowBootstrap(
            settingsService,
            settings,
            appMetadata,
            runtimeTools,
            chdmanPathResolver,
            workflowOrchestrator,
            externalLinkService,
            postConversionArtifacts,
            appFeatureService,
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
