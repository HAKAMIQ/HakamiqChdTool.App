namespace HakamiqChdTool.App.ViewModels.Virtualization;

public readonly record struct VisibleQueueWindow
{
    public VisibleQueueWindow(int firstIndex, int lastIndex, int count)
    {
        if (count <= 0 || firstIndex < 0 || lastIndex < firstIndex)
        {
            FirstIndex = -1;
            LastIndex = -1;
            Count = 0;
            return;
        }

        long expectedLastIndex = (long)firstIndex + count - 1;
        if (expectedLastIndex > int.MaxValue || expectedLastIndex != lastIndex)
        {
            FirstIndex = -1;
            LastIndex = -1;
            Count = 0;
            return;
        }

        FirstIndex = firstIndex;
        LastIndex = lastIndex;
        Count = count;
    }

    public int FirstIndex { get; }

    public int LastIndex { get; }

    public int Count { get; }

    public static VisibleQueueWindow Empty { get; } = new(-1, -1, 0);

    public bool Contains(int index)
    {
        return Count > 0
               && index >= FirstIndex
               && index <= LastIndex;
    }

    public void Deconstruct(
        out int firstIndex,
        out int lastIndex,
        out int count)
    {
        firstIndex = FirstIndex;
        lastIndex = LastIndex;
        count = Count;
    }
}