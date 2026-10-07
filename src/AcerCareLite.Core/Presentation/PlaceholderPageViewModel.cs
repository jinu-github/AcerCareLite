namespace AcerCareLite.Core.Presentation;

/// <summary>Stand-in for pages that arrive in later phases.</summary>
public sealed class PlaceholderPageViewModel : PageViewModel
{
    public PlaceholderPageViewModel(string title, string glyph, string description, string plannedPhase)
        : base(title, glyph)
    {
        Description = description;
        PlannedPhase = plannedPhase;
    }

    public string Description { get; }
    public string PlannedPhase { get; }
}
