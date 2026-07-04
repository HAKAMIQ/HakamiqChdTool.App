using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    private QueueExecutionProfile _executionProfile = QueueExecutionProfile.Standard;

    public QueueExecutionProfile ExecutionProfile
    {
        get => _executionProfile;
        set
        {
            if (SetField(ref _executionProfile, value))
            {
                OnPropertyChanged(nameof(UsesQuickProfile));
            }
        }
    }

    public bool UsesQuickProfile =>
        ExecutionProfile is QueueExecutionProfile.QuickConvert
            or QueueExecutionProfile.QuickExtract
            or QueueExecutionProfile.QuickVerify;
}