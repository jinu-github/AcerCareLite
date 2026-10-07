using CommunityToolkit.Mvvm.ComponentModel;

namespace AcerCareLite.Core.Presentation;

/// <summary>Owns the list of pages and the current selection. Suspend/Resume pause page polling while the window is hidden.</summary>
public sealed class ShellViewModel : ObservableObject
{
    private PageViewModel? _currentPage;
    private bool _suspended;

    public ShellViewModel(IEnumerable<PageViewModel> pages)
    {
        Pages = pages.ToList();
        CurrentPage = Pages.FirstOrDefault();
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel? CurrentPage
    {
        get => _currentPage;
        set
        {
            var old = _currentPage;
            if (!SetProperty(ref _currentPage, value)) return;
            if (_suspended) return; // hooks run on Resume for whichever page is current then
            old?.OnNavigatedFrom();
            value?.OnNavigatedTo();
        }
    }

    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        _currentPage?.OnNavigatedFrom();
    }

    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        _currentPage?.OnNavigatedTo();
    }
}
