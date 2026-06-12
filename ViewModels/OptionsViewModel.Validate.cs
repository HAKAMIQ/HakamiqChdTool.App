using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    [CustomValidation(typeof(OptionsViewModel), nameof(ValidatePendingWorkspaceCustomRoot))]
    public string PendingWorkspaceCustomRootValidationProxy => PendingWorkspaceCustomRoot;

    public void ValidateForSave()
    {
        ValidateProperty(PendingWorkspaceCustomRootValidationProxy, nameof(PendingWorkspaceCustomRootValidationProxy));
        ValidateProperty(CustomOutputRootValidationProxy, nameof(CustomOutputRootValidationProxy));
        ValidateProperty(RedumpDatXmlPathValidationProxy, nameof(RedumpDatXmlPathValidationProxy));
        ValidateProperty(ExternalChdmanPathValidationProxy, nameof(ExternalChdmanPathValidationProxy));
        ValidateProperty(RedumpDatabaseDownloadUrlValidationProxy, nameof(RedumpDatabaseDownloadUrlValidationProxy));
        ValidateProperty(SelectedProcessorValueValidationProxy, nameof(SelectedProcessorValueValidationProxy));
        ValidateProperty(SelectedConcurrentConversionValueValidationProxy, nameof(SelectedConcurrentConversionValueValidationProxy));
    }

    public string? GetFirstErrorMessage()
    {
        if (UseCustomPendingWorkspace)
        {
            if (string.IsNullOrWhiteSpace(PendingWorkspaceCustomRoot))
            {
                return ArabicUi.Get("LocAdv_ErrorPendingWorkspaceRequired");
            }

            if (!Path.IsPathFullyQualified(PendingWorkspaceCustomRoot.Trim()))
            {
                return ArabicUi.Get("LocAdv_ErrorPendingWorkspaceMustBeFullPath");
            }
        }

        string[] propertyNames =
        [
            nameof(PendingWorkspaceCustomRootValidationProxy),
            nameof(CustomOutputRootValidationProxy),
            nameof(RedumpDatXmlPathValidationProxy),
            nameof(ExternalChdmanPathValidationProxy),
            nameof(RedumpDatabaseDownloadUrlValidationProxy),
            nameof(SelectedProcessorValueValidationProxy),
            nameof(SelectedConcurrentConversionValueValidationProxy)
        ];

        foreach (string propertyName in propertyNames)
        {
            string? message = GetErrors(propertyName)
                .Select(static result => result.ErrorMessage)
                .FirstOrDefault(static messageText => !string.IsNullOrWhiteSpace(messageText));

            if (message is not null)
            {
                return message;
            }
        }

        return null;
    }

    private static bool IsFullyQualifiedPathText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Path.IsPathFullyQualified(value.Trim());
    }

    private static bool IsPathTextValid(string? value)
    {
        return IsFullyQualifiedPathText(value);
    }

    public static ValidationResult? ValidatePendingWorkspaceCustomRoot(string _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorPendingWorkspaceValidationFailed");
        }

        if (!vm.UseCustomPendingWorkspace)
        {
            return ValidationResult.Success;
        }

        if (string.IsNullOrWhiteSpace(vm.PendingWorkspaceCustomRoot))
        {
            return Error("LocAdv_ErrorPendingWorkspaceRequired", nameof(PendingWorkspaceCustomRoot));
        }

        return Path.IsPathFullyQualified(vm.PendingWorkspaceCustomRoot.Trim())
            ? ValidationResult.Success
            : Error("LocAdv_ErrorPendingWorkspaceMustBeFullPath", nameof(PendingWorkspaceCustomRoot));
    }

    public static ValidationResult? ValidateCustomOutputRoot(string _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorCustomOutputValidationFailed");
        }

        if (!vm.UseCustomOutputRoot)
        {
            return ValidationResult.Success;
        }

        return IsPathTextValid(vm.CustomOutputRoot)
            ? ValidationResult.Success
            : Error("LocAdv_ErrorCustomOutputRequired", nameof(CustomOutputRoot));
    }

    public static ValidationResult? ValidateRedumpDatXmlPath(string _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorRedumpDatValidationFailed");
        }

        if (string.IsNullOrWhiteSpace(vm.RedumpDatXmlPath))
        {
            return ValidationResult.Success;
        }

        string extension = Path.GetExtension(vm.RedumpDatXmlPath.Trim());
        return extension.Equals(".dat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            ? ValidationResult.Success
            : Error("LocAdv_ErrorRedumpDatExtension", nameof(RedumpDatXmlPath));
    }

    public static ValidationResult? ValidateRedumpDatabaseDownloadUrl(string _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorRedumpDownloadUrlValidationFailed");
        }

        if (string.IsNullOrWhiteSpace(vm.RedumpDatabaseDownloadUrl))
        {
            return ValidationResult.Success;
        }

        bool ok = Uri.TryCreate(vm.RedumpDatabaseDownloadUrl.Trim(), UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo);

        return ok
            ? ValidationResult.Success
            : Error("LocAdv_ErrorRedumpDownloadUrlInvalid", nameof(RedumpDatabaseDownloadUrl));
    }

    public static ValidationResult? ValidateExternalChdmanPath(string _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorExternalChdmanValidationFailed", nameof(ExternalChdmanPath));
        }

        if (vm.UseBundledChdman)
        {
            return ValidationResult.Success;
        }

        string externalChdmanPath = vm.ExternalChdmanPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(externalChdmanPath))
        {
            return Error("LocAdv_ErrorExternalChdmanRequired", nameof(ExternalChdmanPath));
        }

        return ChdmanPathResolver.TryResolveExternal(externalChdmanPath, out string? _)
            ? ValidationResult.Success
            : Error("LocAdv_ErrorExternalChdmanInvalid", nameof(ExternalChdmanPath));
    }

    public static ValidationResult? ValidateSelectedProcessorValue(int _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorProcessorValidationFailed");
        }

        return vm.SelectedProcessorOption is null
            ? Error("LocAdv_ErrorProcessorRequired", nameof(SelectedProcessorOption))
            : ValidationResult.Success;
    }


    public static ValidationResult? ValidateSelectedConcurrentConversionValue(int _, ValidationContext context)
    {
        if (context.ObjectInstance is not OptionsViewModel vm)
        {
            return Error("LocAdv_ErrorConcurrentConversionsValidationFailed");
        }

        int selected = vm.SelectedConcurrentConversionOption?.Value ?? 0;
        bool ok = selected >= AppSettings.DefaultMaxConcurrentConversions
            && selected <= AppSettings.MaxConcurrentConversionsUpperBound;

        return ok
            ? ValidationResult.Success
            : Error("LocAdv_ErrorConcurrentConversionsRequired", nameof(SelectedConcurrentConversionOption));
    }

    private static ValidationResult Error(string key, params string[] memberNames)
    {
        return new ValidationResult(ArabicUi.Get(key), memberNames);
    }
}