namespace AprsLive.Audio;

/// <summary>One selectable audio device. <see cref="Id"/> is what gets passed back to <c>Start</c>; <see cref="Name"/> is for display.</summary>
public readonly record struct AudioDeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}
