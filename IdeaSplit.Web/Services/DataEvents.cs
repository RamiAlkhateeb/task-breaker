namespace IdeaSplit.Web.Services;

/// <summary>Tells open pages to reload after data changed outside them (e.g. repeating tasks were added).</summary>
public sealed class DataEvents
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
