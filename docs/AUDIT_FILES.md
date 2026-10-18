# Production file ledger

One row per production C#/XAML file (excludes `obj/`, `bin/`, `artifacts/` and the test project). Depth classes are defined in `docs/AUDIT_COVERAGE.md`. **PATTERN is never counted as a review.** Maintained by hand-run script; update a row only after the file was actually read (FULL) or the cited range read (PARTIAL).

Totals: FULL 180, PARTIAL 12, PATTERN 225, UNINSPECTED 98 of 515 files.

| Path | Lines | Depth | Note |
| --- | --- | --- | --- |
| `App.xaml` | 91 | UNINSPECTED |  |
| `App.xaml.cs` | 474 | FULL | pass 5: read in full, no defect |
| `Converters/ActionArConv.cs` | 19 | UNINSPECTED |  |
| `Converters/BidiConv.cs` | 20 | UNINSPECTED |  |
| `Converters/BoolVisConv.cs` | 36 | UNINSPECTED |  |
| `Converters/EmptyVisConv.cs` | 49 | UNINSPECTED |  |
| `Converters/ProcBrushConv.cs` | 56 | UNINSPECTED |  |
| `Converters/StateArConv.cs` | 38 | UNINSPECTED |  |
| `Core/Advisory/FmtAdvice.cs` | 23 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Advisory/FmtLevel.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Chd/ChdMetaTags.cs` | 51 | FULL | pass 5: read in full, no defect |
| `Core/Chd/Commands/ChdCmdBuild.cs` | 55 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Chd/Commands/ChdProgParse.cs` | 76 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Chd/Commands/ChdResultCls.cs` | 27 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Chd/Profiles/ChdCommandKind.cs` | 7 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Chd/Profiles/ChdPrepKind.cs` | 7 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Chd/Profiles/ChdProfile.cs` | 12 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Chd/Profiles/ChdProfiles.cs` | 245 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Disc/CueFileRead.cs` | 89 | FULL | pass 5: read in full, no defect |
| `Core/Input/IInputResolver.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Input/IMediaClass.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Input/IMediaPipe.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Input/InputResolver.cs` | 383 | FULL | earlier passes |
| `Core/Input/MediaClass.cs` | 513 | FULL | pass 5: read in full, no defect |
| `Core/Input/MediaDesc.cs` | 15 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Input/MediaInputKind.cs` | 15 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Input/MediaInputProbeStatus.cs` | 13 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Input/MediaInputRoles.cs` | 90 | FULL | pass 5: read in full, no defect |
| `Core/Input/MediaPipe.cs` | 148 | FULL | pass 5: read in full, no defect |
| `Core/Input/MediaPipeDecision.cs` | 53 | FULL | pass 5: read in full, no defect |
| `Core/Input/QueueInClass.cs` | 84 | FULL | pass 5: read in full, no defect |
| `Core/Library/DatClrMame.cs` | 324 | FULL | pass 5: read in full, no defect |
| `Core/Library/DatMatcher.cs` | 452 | FULL | pass 5: read in full, no defect |
| `Core/Library/DatParser.cs` | 412 | FULL | pass 5: read in full, no defect |
| `Core/Library/DatTypes.cs` | 118 | FULL | pass 5: read in full, no defect |
| `Core/Library/LibIndex.cs` | 301 | FULL | pass 5: read in full, no defect |
| `Core/Library/LibPathRelation.cs` | 93 | FULL | pass 5: read in full, no defect |
| `Core/Library/OrgPlanner.cs` | 821 | FULL | pass 5: read in full, no defect |
| `Core/Queue/ChdQueueItem.cs` | 55 | FULL | earlier passes |
| `Core/Queue/IQStateSink.cs` | 36 | FULL | pass 5: read in full, no defect |
| `Core/Queue/IQueueManager.cs` | 29 | FULL | pass 5: read in full, no defect |
| `Core/Queue/QConcurrency.cs` | 565 | FULL | earlier passes |
| `Core/Queue/QNotifyPub.cs` | 42 | FULL | pass 5: read in full, no defect |
| `Core/Queue/QStateEnums.cs` | 37 | FULL | pass 5: read in full, no defect |
| `Core/Queue/QTransSvc.cs` | 249 | FULL | pass 5: read in full, no defect |
| `Core/Queue/QueueCtrl.cs` | 348 | FULL | earlier passes |
| `Core/Queue/QueueManager.cs` | 72 | FULL | earlier passes |
| `Core/Queue/QueueRtState.cs` | 74 | FULL | earlier passes |
| `Core/Queue/QueueSnap.cs` | 18 | FULL | pass 5: read in full, no defect |
| `Core/Queue/QueueStore.cs` | 258 | FULL | earlier passes |
| `Core/Session/QProgAgg.cs` | 49 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Session/RunMetricCalc.cs` | 78 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Session/RunMetrics.cs` | 27 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Session/RunSummary.cs` | 20 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Verification/DiscPolicy.cs` | 380 | FULL | pass 5: read in full, no defect |
| `Core/Verification/DiscPolicyTypes.cs` | 131 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/ActiveWorkflowRegistry.cs` | 34 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/ArchivePrep.cs` | 272 | FULL | earlier passes |
| `Core/Workflow/ChdFlowComp.cs` | 42 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/ChdFlowCtx.cs` | 57 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/ChdFlowExec.cs` | 104 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/ChdFlowMode.cs` | 7 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/ChdFlowOrch.cs` | 250 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/ChdProgReport.cs` | 280 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/ChdTaskRequest.cs` | 21 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/Extraction/CueRefRead.cs` | 205 | FULL | pass 5: read in full |
| `Core/Workflow/Extraction/ExtOutBundle.cs` | 35 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/Extraction/ExtOutContract.cs` | 27 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/Extraction/ExtOutKind.cs` | 7 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/Extraction/ExtOutProof.cs` | 289 | FULL | pass 5: read in full |
| `Core/Workflow/Extraction/ExtOutValid.cs` | 1082 | FULL | pass 5: read in full |
| `Core/Workflow/FlowCleanup.cs` | 503 | FULL | earlier passes |
| `Core/Workflow/FlowConv.cs` | 1658 | FULL | pass 5: read in full |
| `Core/Workflow/FlowExec.cs` | 164 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/FlowExtract.cs` | 756 | FULL | earlier passes |
| `Core/Workflow/FlowInput.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowIssue.cs` | 22 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowOutClean.cs` | 324 | FULL | earlier passes |
| `Core/Workflow/FlowOutPath.cs` | 841 | FULL | pass 5; F25 promotion ownership |
| `Core/Workflow/FlowPathHelp.cs` | 9 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowPost.cs` | 50 | FULL | pass 5: read in full |
| `Core/Workflow/FlowPreRes.cs` | 36 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowPreStage.cs` | 425 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/FlowPrepRes.cs` | 37 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowPrepSvc.cs` | 118 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/FlowProg.cs` | 31 | FULL | pass 5: read in full |
| `Core/Workflow/FlowResult.cs` | 38 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowRuntime.cs` | 159 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/FlowSeverity.cs` | 8 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/FlowSrcClean.cs` | 729 | FULL | earlier passes |
| `Core/Workflow/FlowStageGate.cs` | 146 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/FlowVerify.cs` | 237 | FULL | pass 5: read in full, no defect |
| `Core/Workflow/IChdFlowOrch.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/Paths/FlowPathUtil.cs` | 138 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/Paths/OutPathPlan.cs` | 211 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/Paths/PendingPath.cs` | 119 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/Paths/PendingWsPath.cs` | 346 | FULL | pass 5: read in full |
| `Core/Workflow/Paths/SafePathValid.cs` | 715 | FULL | earlier passes |
| `Core/Workflow/Paths/SrcPathRes.cs` | 64 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/Progress/ExtByteProg.cs` | 139 | FULL | pass 5: read in full, no defect (wrappers with no production caller noted in AUDIT_COVERAGE) |
| `Core/Workflow/Progress/FlowProgMode.cs` | 9 | FULL | pass 5: read in full (types/records), no defect |
| `Core/Workflow/Progress/FlowProgSample.cs` | 11 | FULL | pass 5: read in full (types/records), no defect |
| `GlobalChd.cs` | 2 | UNINSPECTED |  |
| `Localization/AccessibleText.cs` | 123 | UNINSPECTED |  |
| `Localization/AppLangSvc.cs` | 311 | UNINSPECTED |  |
| `Localization/ArabicUi.cs` | 446 | UNINSPECTED |  |
| `Localization/BidiText.cs` | 108 | UNINSPECTED |  |
| `Localization/DeepHashView.cs` | 182 | UNINSPECTED |  |
| `Localization/LegacyStatus.cs` | 360 | UNINSPECTED |  |
| `Localization/MainMsgs.cs` | 151 | UNINSPECTED |  |
| `Localization/MainUiKeys.cs` | 50 | UNINSPECTED |  |
| `Localization/TaskActions.cs` | 14 | UNINSPECTED |  |
| `Localization/TaskQStates.cs` | 27 | UNINSPECTED |  |
| `Localization/TaskResults.cs` | 18 | UNINSPECTED |  |
| `Models/AboutInfo.cs` | 40 | UNINSPECTED |  |
| `Models/AppMetadata.cs` | 75 | UNINSPECTED |  |
| `Models/Chd/ChdCapsSnap.cs` | 73 | UNINSPECTED |  |
| `Models/Chd/ChdCompRes.cs` | 22 | UNINSPECTED |  |
| `Models/Chd/ChdConvResult.cs` | 66 | UNINSPECTED |  |
| `Models/Chd/ChdConvState.cs` | 9 | UNINSPECTED |  |
| `Models/Chd/ChdExtKind.cs` | 10 | UNINSPECTED |  |
| `Models/Chd/ChdInfoResult.cs` | 76 | UNINSPECTED |  |
| `Models/Chd/ChdProcPrio.cs` | 7 | UNINSPECTED |  |
| `Models/Chd/ChdVResult.cs` | 35 | UNINSPECTED |  |
| `Models/Chd/ConvPerfMode.cs` | 9 | UNINSPECTED |  |
| `Models/Chd/ConvPerfRep.cs` | 38 | UNINSPECTED |  |
| `Models/Chd/IsoCmdOver.cs` | 8 | UNINSPECTED |  |
| `Models/Chd/PerfSample.cs` | 26 | UNINSPECTED |  |
| `Models/Configuration/AppSettings.cs` | 256 | UNINSPECTED |  |
| `Models/GameDiscSet.cs` | 37 | UNINSPECTED |  |
| `Models/GameEntry.cs` | 34 | UNINSPECTED |  |
| `Models/GameEntryType.cs` | 14 | UNINSPECTED |  |
| `Models/IntegState.cs` | 24 | UNINSPECTED |  |
| `Models/MultiDiscItem.cs` | 30 | UNINSPECTED |  |
| `Models/MultiDiscSet.cs` | 29 | UNINSPECTED |  |
| `Models/NameConf.cs` | 9 | UNINSPECTED |  |
| `Models/OrphanItem.cs` | 35 | UNINSPECTED |  |
| `Models/OrphanScan.cs` | 89 | UNINSPECTED |  |
| `Models/PendingMode.cs` | 7 | UNINSPECTED |  |
| `Models/PlatformResult.cs` | 42 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdCheckCode.cs` | 15 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdCheckResult.cs` | 6 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdCompEst.cs` | 26 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdCompRate.cs` | 11 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdIsoResult.cs` | 15 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdProfile.cs` | 8 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdProgress.cs` | 15 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdPs3Meta.cs` | 36 | UNINSPECTED |  |
| `Models/PlayStation/BluRayAnalysis/BdStage.cs` | 13 | UNINSPECTED |  |
| `Models/PostArtFail.cs` | 38 | UNINSPECTED |  |
| `Models/PostArtResult.cs` | 142 | UNINSPECTED |  |
| `Models/ProcState.cs` | 12 | UNINSPECTED |  |
| `Models/ProgressEvent.cs` | 40 | UNINSPECTED |  |
| `Models/QAdvAction.cs` | 13 | UNINSPECTED |  |
| `Models/QAdvReason.cs` | 31 | UNINSPECTED |  |
| `Models/QAdvSeverity.cs` | 9 | UNINSPECTED |  |
| `Models/QExecProfile.cs` | 9 | UNINSPECTED |  |
| `Models/QIngestKind.cs` | 7 | UNINSPECTED |  |
| `Models/QIntakeAdv.cs` | 79 | UNINSPECTED |  |
| `Models/QIntakeSrc.cs` | 10 | UNINSPECTED |  |
| `Models/QProgSnap.cs` | 36 | UNINSPECTED |  |
| `Properties/AssemblyInfo.cs` | 10 | UNINSPECTED |  |
| `QueueRun/IRunCoord.cs` | 34 | FULL | pass 5: read in full, no defect |
| `QueueRun/IRunUiPort.cs` | 67 | FULL | pass 5: read in full, no defect |
| `QueueRun/RunCoord.cs` | 789 | FULL | earlier passes |
| `Resources/AppConverters.xaml` | 16 | UNINSPECTED |  |
| `Resources/ArabicStrings.xaml` | 1828 | UNINSPECTED |  |
| `Resources/EnglishStrings.xaml` | 1834 | UNINSPECTED |  |
| `Resources/FluentIcons.xaml` | 38 | UNINSPECTED |  |
| `Resources/Style/ComboBox.xaml` | 355 | UNINSPECTED |  |
| `Resources/Style/Fluent/BaseStyles.xaml` | 87 | UNINSPECTED |  |
| `Resources/Style/Fluent/ButtonStyles.xaml` | 630 | UNINSPECTED |  |
| `Resources/Style/Fluent/CardStyles.xaml` | 253 | UNINSPECTED |  |
| `Resources/Style/Fluent/NavStyles.xaml` | 361 | UNINSPECTED |  |
| `Resources/Style/Fluent/QueueStyles.xaml` | 412 | UNINSPECTED |  |
| `Resources/Style/InputCheck.xaml` | 267 | UNINSPECTED |  |
| `Resources/Style/OptsStyles.xaml` | 953 | UNINSPECTED |  |
| `Resources/Style/QItemTpl.xaml` | 8 | UNINSPECTED |  |
| `Resources/Style/Queue/QIActionsTpl.xaml` | 348 | UNINSPECTED |  |
| `Resources/Style/Queue/QIHeaderTpl.xaml` | 309 | UNINSPECTED |  |
| `Resources/Style/Queue/QIProgTpl.xaml` | 73 | UNINSPECTED |  |
| `Resources/Style/Queue/QIRedTpl.xaml` | 258 | UNINSPECTED |  |
| `Resources/Style/Queue/QIStatusTpl.xaml` | 192 | UNINSPECTED |  |
| `Resources/Style/Queue/QItemBase.xaml` | 232 | UNINSPECTED |  |
| `Resources/Style/Queue/QItemTpl.xaml` | 61 | UNINSPECTED |  |
| `Resources/Style/Queue/QWShared.xaml` | 549 | UNINSPECTED |  |
| `Resources/Style/ScrollBar.xaml` | 214 | UNINSPECTED |  |
| `Resources/Style/Win11UiPolish.xaml` | 60 | UNINSPECTED |  |
| `Resources/Style/Workspace.xaml` | 17 | UNINSPECTED |  |
| `Resources/Themes/Controls/ButtonStyles.xaml` | 458 | UNINSPECTED |  |
| `Resources/Themes/Controls/CardStyles.xaml` | 87 | UNINSPECTED |  |
| `Resources/Themes/Controls/TextStyles.xaml` | 214 | UNINSPECTED |  |
| `Resources/Themes/Dark.xaml` | 612 | PARTIAL | pass 6: theme token/palette sections and current contrast gate; not full dictionary review |
| `Resources/Themes/Fluent/Controls.xaml` | 464 | UNINSPECTED |  |
| `Resources/Themes/Light.xaml` | 609 | PARTIAL | pass 6: theme token/palette sections and current contrast gate; not full dictionary review |
| `Resources/Tokens/Elevation.xaml` | 40 | UNINSPECTED |  |
| `Resources/Tokens/Radius.xaml` | 61 | UNINSPECTED |  |
| `Resources/Tokens/Spacing.xaml` | 297 | PARTIAL | pass 6: layout/window token sections; remaining tokens not read |
| `Resources/Tokens/Typography.xaml` | 82 | UNINSPECTED |  |
| `Services/AboutFactory.cs` | 167 | PATTERN | search only |
| `Services/AppLogger.cs` | 231 | FULL | pass 5: read in full, no defect |
| `Services/AppPaths.cs` | 568 | FULL | pass 5: read in full, no defect |
| `Services/ArchiveCand.cs` | 843 | FULL | pass 5: read in full, no defect |
| `Services/ArchiveDep.cs` | 39 | FULL | pass 5: read in full |
| `Services/ArchiveInteg.cs` | 10 | FULL | pass 5: read in full |
| `Services/ArchiveName.cs` | 22 | FULL | pass 5: read in full |
| `Services/ArchiveResourcePolicy.cs` | 141 | FULL | pass 5: read in full |
| `Services/ArchiveResult.cs` | 25 | FULL | pass 5: read in full |
| `Services/ArchiveSvc.cs` | 214 | FULL | pass 5: read in full |
| `Services/BinCueRescue/BinProbeRes.cs` | 5 | PATTERN | search only |
| `Services/BinCueRescue/BinSectorProbe.cs` | 592 | PATTERN | search only |
| `Services/BinCueRescue/BinTrackKind.cs` | 11 | PATTERN | search only |
| `Services/BinCueRescue/CueFlowAdapt.cs` | 982 | PARTIAL | pass 5: lines 1-700 read; remainder is path helpers |
| `Services/BinCueRescue/CueRefusal.cs` | 14 | PATTERN | search only |
| `Services/BinCueRescue/CueResDecision.cs` | 8 | PATTERN | search only |
| `Services/BinCueRescue/CueResPlan.cs` | 39 | PATTERN | search only |
| `Services/BinCueRescue/CueResWriter.cs` | 1242 | PARTIAL | pass 5: lines 1-560 and 640-1242 read; 560-640 (atomic temp write) not |
| `Services/BinCueRescue/CueTrackPlan.cs` | 41 | PATTERN | search only |
| `Services/BinCueRescue/CueWriteOpts.cs` | 11 | PATTERN | search only |
| `Services/BinCueRescue/CueWriteRes.cs` | 62 | PATTERN | search only |
| `Services/BinCueRescue/MultiBinAsm.cs` | 1106 | PATTERN | search only |
| `Services/Chd/ChdProbeFmt.cs` | 252 | PATTERN | search only |
| `Services/ChdCliRun.cs` | 251 | FULL | earlier passes |
| `Services/ChdInfoService.cs` | 592 | FULL | pass 5: read in full, no defect |
| `Services/ChdOpLog.cs` | 87 | PATTERN | search only |
| `Services/ChdOutParse.cs` | 45 | PATTERN | search only |
| `Services/ChdPathRes.cs` | 68 | PATTERN | search only |
| `Services/ChdProcRun.cs` | 969 | FULL | earlier passes |
| `Services/ChdProfPlan.cs` | 1076 | PATTERN | search only |
| `Services/ChdVerifySvc.cs` | 517 | FULL | pass 5: read in full, no defect |
| `Services/CleanupService.cs` | 549 | FULL | earlier passes |
| `Services/Configuration/AppSetSvc.cs` | 460 | FULL | pass 5: read in full; open: corrupt config is replaced by defaults on next save (no backup) |
| `Services/ConsoleAlias.cs` | 372 | PATTERN | search only |
| `Services/ConsoleId.cs` | 371 | PATTERN | search only |
| `Services/ConsoleMedia/DiscIdResult.cs` | 58 | PATTERN | search only |
| `Services/ConsoleMedia/DiscIdSvc.cs` | 53 | PATTERN | search only |
| `Services/ConsoleMedia/DiscScanCtx.cs` | 390 | PATTERN | search only |
| `Services/ConsoleMedia/IDiscIdProbe.cs` | 7 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/DreamcastProbe.cs` | 24 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/MegaCdProbe.cs` | 23 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/NeoGeoProbe.cs` | 22 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/PathHintProbe.cs` | 46 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/PceCdProbe.cs` | 22 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/Ps1Probe.cs` | 50 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/SaturnProbe.cs` | 21 | PATTERN | search only |
| `Services/ConsoleMedia/Probes/ThreeDoProbe.cs` | 23 | PATTERN | search only |
| `Services/ConvPathVal.cs` | 195 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdCapsSvc.cs` | 533 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdCmdPrep.cs` | 663 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdConvMsgs.cs` | 25 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdConvSvc.cs` | 686 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdExecSetup.cs` | 190 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdExecSvc.cs` | 70 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdMediaPol.cs` | 217 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdOpGate.cs` | 225 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdOutPath.cs` | 75 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdProfilePol.cs` | 98 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdProgParse.cs` | 262 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdProgPolicy.cs` | 29 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdReqGuard.cs` | 40 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdResultMap.cs` | 426 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdSvcSupport.cs` | 826 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ChdVBridge.cs` | 450 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ConvGuard.cs` | 255 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ConvMetrics.cs` | 50 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ConvPerfFact.cs` | 202 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/ConvSafe.cs` | 103 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/CsoPrep.cs` | 664 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/CsoPrepCoord.cs` | 135 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/CsoTempWs.cs` | 93 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/IChdCapsSvc.cs` | 20 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/IChdPipeline.cs` | 119 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/MetaExtract.cs` | 270 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/PlatformChd.cs` | 461 | FULL | pass 5: read in full, no defect |
| `Services/Conversion/RestorePolicy.cs` | 76 | FULL | pass 5: read in full, no defect |
| `Services/CpuTopology.cs` | 190 | PATTERN | search only |
| `Services/DeepHash.cs` | 826 | PARTIAL | line ranges in AUDIT_COVERAGE.md |
| `Services/DiagLog.cs` | 26 | PATTERN | search only |
| `Services/DiscKindRes.cs` | 78 | PATTERN | search only |
| `Services/DiscLayout/DiscLayout.cs` | 117 | PATTERN | search only |
| `Services/DiscMediaKind.cs` | 8 | PATTERN | search only |
| `Services/DiscMetaProbe.cs` | 778 | PATTERN | search only |
| `Services/DiscSerials.cs` | 543 | PATTERN | search only |
| `Services/DiskEstimate.cs` | 51 | PATTERN | search only |
| `Services/DiskPreSvc.cs` | 776 | FULL | pass 5: read in full, no defect |
| `Services/DiskReq.cs` | 21 | PATTERN | search only |
| `Services/ExplorerHelp.cs` | 281 | FULL | pass 5: read in full, no defect |
| `Services/ExtLinkSvc.cs` | 101 | FULL | pass 5: read in full, no defect |
| `Services/ExternalTools/CsoToolLocator.cs` | 187 | FULL | pass 5: read in full |
| `Services/ExternalTools/CsoToolProbe.cs` | 153 | PATTERN | search only |
| `Services/ExternalTools/ToolProcRun.cs` | 249 | FULL | pass 5: read in full |
| `Services/FileHashSvc.cs` | 107 | FULL | pass 5: read in full, no defect |
| `Services/FileIntegProbe.cs` | 393 | PATTERN | search only |
| `Services/FilePathGate.cs` | 324 | FULL | earlier passes |
| `Services/FmtAdvisor.cs` | 193 | PATTERN | search only |
| `Services/GameCatalog/GameDiscRes.cs` | 252 | PATTERN | search only |
| `Services/GameCatalog/GameMediaScan.cs` | 203 | PATTERN | search only |
| `Services/IExtLinkSvc.cs` | 6 | PATTERN | search only |
| `Services/IOptPickSvc.cs` | 8 | PATTERN | search only |
| `Services/IsoCmdRes.cs` | 158 | PATTERN | search only |
| `Services/Library/DatHashing.cs` | 124 | FULL | pass 6: read in full; bounded streaming hash and cancellation path |
| `Services/Library/LibraryAnalysisService.cs` | 356 | FULL | pass 6: read in full; phase outcomes, partial results and cancellation |
| `Services/M3u/IM3uGen.cs` | 9 | PATTERN | search only |
| `Services/M3u/M3uGen.cs` | 547 | PARTIAL | pass 5: lines 1-260 (generation, atomic write) read |
| `Services/M3u/M3uGenResult.cs` | 83 | PATTERN | search only |
| `Services/M3u/MultiDiscDet.cs` | 76 | PATTERN | search only |
| `Services/MediaInputPolicy/MediaDecision.cs` | 90 | PATTERN | search only |
| `Services/MediaInputPolicy/MediaPolicy.cs` | 184 | PATTERN | search only |
| `Services/NameFixEngine.cs` | 623 | PARTIAL | pass 5: lines 380-623 (rename) read |
| `Services/OrphanClean.cs` | 113 | FULL | earlier passes |
| `Services/OrphanScan.cs` | 516 | FULL | earlier passes |
| `Services/PerfSvc.cs` | 306 | PATTERN | search only |
| `Services/PlatformFilter.cs` | 143 | PATTERN | search only |
| `Services/PlatformSvc.cs` | 1651 | PATTERN | search only |
| `Services/PlayStation/BluRayAnalysis/BdIsoSvc.cs` | 876 | PATTERN | search only |
| `Services/PlayStation/Ps2/Ps2CompatSvc.cs` | 157 | PATTERN | search only |
| `Services/PlayStation/Ps2/Ps2DiscId.cs` | 22 | PATTERN | search only |
| `Services/PlayStation/Ps2/Ps2IdDetect.cs` | 197 | PATTERN | search only |
| `Services/PlayStation/Ps2/Ps2MediaKind.cs` | 11 | PATTERN | search only |
| `Services/PlayStation/Ps2/Ps2Struct.cs` | 24 | PATTERN | search only |
| `Services/PlayStation/Ps2/Ps2StructScan.cs` | 649 | PATTERN | search only |
| `Services/PostProcessing/ISbiCopy.cs` | 8 | PATTERN | search only |
| `Services/PostProcessing/PostArtSvc.cs` | 291 | PATTERN | search only |
| `Services/PostProcessing/SbiCopy.cs` | 425 | FULL | pass 5: read in full |
| `Services/Power/IConvPower.cs` | 10 | PATTERN | search only |
| `Services/Power/WinPowerGuard.cs` | 229 | FULL | pass 5: read in full, no defect |
| `Services/ProcStateMap.cs` | 66 | PATTERN | search only |
| `Services/QConvRules.cs` | 43 | PATTERN | search only |
| `Services/QDropPath.cs` | 183 | PATTERN | search only |
| `Services/QItemOps.cs` | 30 | PATTERN | search only |
| `Services/QOpCapsSvc.cs` | 118 | PATTERN | search only |
| `Services/QOpModeView.cs` | 178 | PATTERN | search only |
| `Services/QVerifyView.cs` | 299 | PATTERN | search only |
| `Services/QueueOpMode.cs` | 9 | PATTERN | search only |
| `Services/RawSerialProbe.cs` | 709 | PATTERN | search only |
| `Services/RedAutoSync.cs` | 153 | PATTERN | search only |
| `Services/RedDisplay.cs` | 168 | PATTERN | search only |
| `Services/RedIndexRes.cs` | 42 | PATTERN | search only |
| `Services/RedLibEntry.cs` | 20 | PATTERN | search only |
| `Services/RedLibIndex.cs` | 851 | PATTERN | search only |
| `Services/RedLibResult.cs` | 40 | PATTERN | search only |
| `Services/RedLibScan.cs` | 313 | PATTERN | search only |
| `Services/RedSqlMgr.cs` | 1518 | PARTIAL | line ranges in AUDIT_COVERAGE.md |
| `Services/RedumpCatalog/RedArtKind.cs` | 15 | PATTERN | search only |
| `Services/RedumpCatalog/RedCatOption.cs` | 70 | PATTERN | search only |
| `Services/RedumpCatalog/RedEndpoint.cs` | 47 | PATTERN | search only |
| `Services/RedumpCatalog/RedSysCatalog.cs` | 323 | PATTERN | search only |
| `Services/RedumpCatalog/RedSysEntry.cs` | 64 | PATTERN | search only |
| `Services/RedumpSync.cs` | 897 | PARTIAL | line ranges in AUDIT_COVERAGE.md |
| `Services/RedumpV2/RedumpV2Classifier.cs` | 304 | PATTERN | search only |
| `Services/RedumpV2/RedumpV2Engine.cs` | 1596 | PATTERN | search only |
| `Services/RedumpV2/RedumpV2Models.cs` | 93 | PATTERN | search only |
| `Services/RedumpV2/RedumpV2Policy.cs` | 206 | PATTERN | search only |
| `Services/RunSummaryFmt.cs` | 92 | PATTERN | search only |
| `Services/RunToolSvc.cs` | 237 | FULL | pass 5: read in full |
| `Services/RuntimeFmt.cs` | 175 | PATTERN | search only |
| `Services/SevenZipRes.cs` | 34 | FULL | pass 5: read in full |
| `Services/SevenZipRun.cs` | 442 | FULL | pass 5: read in full |
| `Services/SevenZipSvc.cs` | 319 | FULL | pass 5: read in full |
| `Services/Storage/StoreDevice.cs` | 278 | PATTERN | search only |
| `Services/Storage/StoreHealth.cs` | 66 | PATTERN | search only |
| `Services/Storage/StoreTemp.cs` | 42 | PATTERN | search only |
| `Services/Storage/StoreTempMon.cs` | 244 | PATTERN | search only |
| `Services/Storage/StoreTopology.cs` | 118 | PATTERN | search only |
| `Services/StressSvc.cs` | 356 | FULL | pass 5: read in full, no defect |
| `Services/ZipExtract.cs` | 1363 | FULL | pass 5; F28 destination must be empty |
| `Services/ZipInspect.cs` | 306 | FULL | pass 5; F29 wildcard entry refused |
| `Startup/MainBoot.cs` | 134 | FULL | pass 5: read in full, no defect |
| `Startup/MainStartup.cs` | 512 | FULL | pass 5: read in full, no defect |
| `Ui/Queue/QueueClear.cs` | 17 | PATTERN | search only |
| `Ui/Queue/QueueModeRes.cs` | 154 | PATTERN | search only |
| `Ui/Queue/QueueName.cs` | 82 | PATTERN | search only |
| `Ui/Queue/QueueUiTracker.cs` | 203 | PATTERN | search only |
| `Ui/Queue/RedNameSvc.cs` | 379 | PATTERN | search only |
| `Ui/Shell/ClipboardSvc.cs` | 28 | PATTERN | search only |
| `Ui/Shell/HqOptionsShell.cs` | 182 | PATTERN | search only |
| `Ui/Shell/HqOptionsTabs.cs` | 132 | PATTERN | search only |
| `Ui/Shell/HqOptionsText.cs` | 60 | PATTERN | search only |
| `Ui/Shell/HqOptionsTips.cs` | 86 | PATTERN | search only |
| `Ui/Shell/HqOptsCmds.cs` | 86 | PATTERN | search only |
| `Ui/Shell/HqOptsLayout.cs` | 91 | PATTERN | search only |
| `Ui/Shell/HqOptsRed.cs` | 292 | PATTERN | search only |
| `Ui/Shell/HqOptsTools.cs` | 492 | PATTERN | search only |
| `Ui/Shell/IResText.cs` | 15 | PATTERN | search only |
| `Ui/Shell/IUiDispatcher.cs` | 20 | PATTERN | search only |
| `Ui/Shell/IWinActivate.cs` | 6 | PATTERN | search only |
| `Ui/Shell/RedDetailsFac.cs` | 22 | PATTERN | search only |
| `Ui/Shell/RedTimer.cs` | 45 | PATTERN | search only |
| `Ui/Shell/ResText.cs` | 53 | PATTERN | search only |
| `Ui/Shell/UiDispatcher.cs` | 82 | PATTERN | search only |
| `Ui/Shell/UiPriority.cs` | 12 | PATTERN | search only |
| `Ui/Shell/UpdateService.cs` | 417 | PATTERN | search only |
| `Ui/Shell/WinActivator.cs` | 11 | PATTERN | search only |
| `Ui/Shell/WindowBackdrop.cs` | 463 | PATTERN | search only |
| `Ui/WpfAdapters/AppRestart.cs` | 531 | PATTERN | search only |
| `Ui/WpfAdapters/OptPickSvc.cs` | 355 | PATTERN | search only |
| `Ui/WpfAdapters/ThemeSvc.cs` | 225 | PATTERN | search only |
| `ViewModels/AboutVM.cs` | 189 | PATTERN | search only |
| `ViewModels/Dialogs/IRedCopy.cs` | 6 | PATTERN | search only |
| `ViewModels/Dialogs/IRedTimer.cs` | 10 | PATTERN | search only |
| `ViewModels/Dialogs/RedDlgParser.cs` | 689 | PATTERN | search only |
| `ViewModels/Dialogs/RedDlgVM.cs` | 437 | PATTERN | search only |
| `ViewModels/IMainSession.cs` | 84 | PATTERN | search only |
| `ViewModels/LibraryAnalysisVM.cs` | 754 | PARTIAL | line ranges in AUDIT_COVERAGE.md |
| `ViewModels/MainVM.Fast.cs` | 240 | FULL | pass 5: read in full, no defect |
| `ViewModels/MainVM.Intake.cs` | 805 | PARTIAL | line ranges in AUDIT_COVERAGE.md |
| `ViewModels/MainVM.Queue.cs` | 1087 | FULL | earlier passes |
| `ViewModels/MainVM.cs` | 313 | FULL | earlier passes |
| `ViewModels/OptChoices.cs` | 80 | PATTERN | search only |
| `ViewModels/OptVM.Browse.cs` | 395 | PATTERN | search only |
| `ViewModels/OptVM.Choice.cs` | 41 | PATTERN | search only |
| `ViewModels/OptVM.RedRpt.cs` | 264 | PATTERN | search only |
| `ViewModels/OptVM.Redump.cs` | 91 | PATTERN | search only |
| `ViewModels/OptVM.Runtime.cs` | 156 | PATTERN | search only |
| `ViewModels/OptVM.Settings.cs` | 174 | PATTERN | search only |
| `ViewModels/OptVM.State.cs` | 122 | PATTERN | search only |
| `ViewModels/OptVM.Tools.cs` | 65 | PATTERN | search only |
| `ViewModels/OptVM.Valid.cs` | 238 | PATTERN | search only |
| `ViewModels/OptionsVM.cs` | 834 | PATTERN | search only |
| `ViewModels/QueueItemVM.cs` | 32 | FULL | pass 5: read in full, no defect |
| `ViewModels/QueueMenuVM.cs` | 104 | PATTERN | search only |
| `ViewModels/QueueSmooth.cs` | 202 | FULL | pass 5: read in full, no defect |
| `ViewModels/RenameCfmVM.cs` | 54 | PATTERN | search only |
| `ViewModels/TaskExec.cs` | 25 | FULL | pass 5: read in full, no defect |
| `ViewModels/TaskIntentMap.cs` | 77 | PATTERN | search only |
| `ViewModels/TaskItemCmds.cs` | 118 | PATTERN | search only |
| `ViewModels/TaskItemProg.cs` | 127 | PATTERN | search only |
| `ViewModels/TaskItemState.cs` | 319 | FULL | pass 5: read in full, no defect |
| `ViewModels/TaskItemVM.cs` | 1326 | PATTERN | search only |
| `ViewModels/TaskItemVerify.cs` | 296 | PATTERN | search only |
| `ViewModels/TaskItemView.cs` | 423 | PATTERN | search only |
| `ViewModels/TaskQAdapter.cs` | 478 | FULL | pass 5: read in full, no defect |
| `ViewModels/TaskRowView.cs` | 1305 | PATTERN | search only |
| `ViewModels/TaskRuntime.cs` | 340 | FULL | pass 5: read in full, no defect |
| `ViewModels/TaskbarProg.cs` | 50 | PATTERN | search only |
| `ViewModels/Virtualization/QViewportSvc.cs` | 467 | FULL | pass 5: read in full, no defect |
| `ViewModels/Virtualization/QueueRowData.cs` | 86 | FULL | pass 5: read in full, no defect |
| `ViewModels/Virtualization/QueueRowStore.cs` | 229 | FULL | earlier passes |
| `ViewModels/Virtualization/VirtQueue.cs` | 392 | FULL | earlier passes |
| `ViewModels/Virtualization/VisibleQueue.cs` | 53 | FULL | pass 5: read in full, no defect |
| `Views/AboutWindow.xaml` | 581 | PATTERN | search only |
| `Views/AboutWindow.xaml.cs` | 119 | PATTERN | search only |
| `Views/ClearLogCfm.xaml` | 233 | PATTERN | search only |
| `Views/ClearLogCfm.xaml.cs` | 86 | PATTERN | search only |
| `Views/LibraryAnalysisWindow.xaml` | 555 | PATTERN | pass 6 layout cases exercised; source still not read in full |
| `Views/LibraryAnalysisWindow.xaml.cs` | 96 | PATTERN | pass 6 layout cases exercised; source still not read in full |
| `Views/Main/ActionRailView.xaml` | 41 | PATTERN | search only |
| `Views/Main/ActionRailView.xaml.cs` | 29 | PATTERN | search only |
| `Views/Main/EmptyQueue.xaml` | 136 | PATTERN | search only |
| `Views/Main/EmptyQueue.xaml.cs` | 25 | PATTERN | search only |
| `Views/Main/Footer.xaml` | 223 | PATTERN | search only |
| `Views/Main/Footer.xaml.cs` | 339 | PATTERN | search only |
| `Views/Main/MainHeaderView.xaml` | 609 | PATTERN | search only |
| `Views/Main/MainHeaderView.xaml.cs` | 110 | PATTERN | search only |
| `Views/Main/OpMode.xaml` | 393 | PATTERN | search only |
| `Views/Main/OpMode.xaml.cs` | 64 | PATTERN | search only |
| `Views/Main/ProcActions.xaml` | 306 | PATTERN | search only |
| `Views/Main/ProcActions.xaml.cs` | 13 | PATTERN | search only |
| `Views/Main/QToolbar.xaml` | 141 | PATTERN | search only |
| `Views/Main/QToolbar.xaml.cs` | 11 | PATTERN | search only |
| `Views/Main/QueueListView.xaml` | 113 | PATTERN | search only |
| `Views/Main/QueueListView.xaml.cs` | 56 | PATTERN | search only |
| `Views/Main/QueueView.xaml` | 175 | PATTERN | search only |
| `Views/Main/QueueView.xaml.cs` | 90 | PATTERN | search only |
| `Views/Main/TrayIcon.xaml` | 154 | PATTERN | search only |
| `Views/Main/TrayIcon.xaml.cs` | 85 | PATTERN | search only |
| `Views/MainWindow/MW.Cmds.cs` | 61 | PATTERN | search only |
| `Views/MainWindow/MW.Dialogs.cs` | 59 | PATTERN | search only |
| `Views/MainWindow/MW.Drag.cs` | 264 | PATTERN | search only |
| `Views/MainWindow/MW.Footer.cs` | 449 | PATTERN | search only |
| `Views/MainWindow/MW.Header.cs` | 156 | PATTERN | search only |
| `Views/MainWindow/MW.Integrity.cs` | 668 | PATTERN | search only |
| `Views/MainWindow/MW.Life.cs` | 327 | FULL | pass 5: read in full; shutdown cleanup runs only after queue quiesced, marker-gated |
| `Views/MainWindow/MW.Modals.cs` | 149 | PATTERN | search only |
| `Views/MainWindow/MW.OpMode.cs` | 108 | PATTERN | search only |
| `Views/MainWindow/MW.Post.cs` | 137 | PATTERN | search only |
| `Views/MainWindow/MW.QPred.cs` | 269 | PATTERN | search only |
| `Views/MainWindow/MW.QUpdate.cs` | 203 | PATTERN | search only |
| `Views/MainWindow/MW.Queue.cs` | 335 | PATTERN | search only |
| `Views/MainWindow/MW.RedCmds.cs` | 530 | PATTERN | search only |
| `Views/MainWindow/MW.RedName.cs` | 168 | PATTERN | search only |
| `Views/MainWindow/MW.RowSync.cs` | 355 | PATTERN | search only |
| `Views/MainWindow/MW.Select.cs` | 423 | PATTERN | search only |
| `Views/MainWindow/MW.Session.cs` | 216 | PATTERN | search only |
| `Views/MainWindow/MW.Shutdown.cs` | 219 | FULL | pass 5: read in full; shutdown cleanup runs only after queue quiesced, marker-gated |
| `Views/MainWindow/MW.Startup.cs` | 100 | PATTERN | search only |
| `Views/MainWindow/MW.Taskbar.cs` | 55 | PATTERN | search only |
| `Views/MainWindow/MW.Text.cs` | 16 | PATTERN | search only |
| `Views/MainWindow/MW.Tray.cs` | 101 | PATTERN | search only |
| `Views/MainWindow/MW.UiPort.cs` | 171 | PATTERN | search only |
| `Views/MainWindow/MW.UiState.cs` | 389 | PATTERN | search only |
| `Views/MainWindow/MainWindow.xaml` | 157 | PATTERN | search only |
| `Views/MainWindow/MainWindow.xaml.cs` | 267 | PATTERN | search only |
| `Views/Options/ExtTools.xaml` | 333 | PATTERN | search only |
| `Views/Options/ExtTools.xaml.cs` | 34 | PATTERN | search only |
| `Views/Options/PathsView.xaml` | 959 | PATTERN | search only |
| `Views/Options/PathsView.xaml.cs` | 11 | PATTERN | search only |
| `Views/Options/PerfView.xaml` | 221 | PATTERN | search only |
| `Views/Options/PerfView.xaml.cs` | 11 | PATTERN | search only |
| `Views/Options/ProcView.xaml` | 413 | PATTERN | search only |
| `Views/Options/ProcView.xaml.cs` | 11 | PATTERN | search only |
| `Views/Options/RedumpView.xaml` | 728 | PATTERN | search only |
| `Views/Options/RedumpView.xaml.cs` | 57 | PATTERN | search only |
| `Views/OptionsWindow.xaml` | 509 | PATTERN | search only |
| `Views/OptionsWindow.xaml.cs` | 169 | PATTERN | search only |
| `Views/OrphanClean.xaml` | 538 | PATTERN | search only |
| `Views/OrphanClean.xaml.cs` | 90 | PATTERN | search only |
| `Views/RedDetails.xaml` | 1013 | PATTERN | search only |
| `Views/RedDetails.xaml.cs` | 73 | PATTERN | search only |
| `Views/RedNotice.xaml` | 248 | PATTERN | search only |
| `Views/RedNotice.xaml.cs` | 93 | PATTERN | search only |
| `Views/RenameCfm.xaml` | 273 | PATTERN | search only |
| `Views/RenameCfm.xaml.cs` | 80 | PATTERN | search only |
| `Views/VerifyResult.xaml` | 427 | PATTERN | search only |
| `Views/VerifyResult.xaml.cs` | 56 | PATTERN | search only |
