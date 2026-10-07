using CommunityToolkit.Mvvm.ComponentModel;

namespace AcerCareLite.Core.Presentation;

/// <summary>Base class for every page shown in the shell. No WPF types here, so it is unit-testable.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string glyph)
    {
        Title = title;
        Glyph = glyph;
    }

    public string Title { get; }

    /// <summary>Segoe MDL2 Assets code point used as the navigation icon.</summary>
    public string Glyph { get; }

    /// <summary>Pages start monitoring here and stop in OnNavigatedFrom, so hidden pages cost nothing.</summary>
    public virtual void OnNavigatedTo() { }
    public virtual void OnNavigatedFrom() { }
}
