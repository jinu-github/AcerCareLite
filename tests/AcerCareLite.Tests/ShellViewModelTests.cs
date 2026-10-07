using AcerCareLite.Core.Presentation;
using Xunit;

namespace AcerCareLite.Tests;

public class ShellViewModelTests
{
    private sealed class TestPage : PageViewModel
    {
        public int Entered { get; private set; }
        public int Left { get; private set; }
        public TestPage(string title) : base(title, "\uE80F") { }
        public override void OnNavigatedTo() => Entered++;
        public override void OnNavigatedFrom() => Left++;
    }

    [Fact]
    public void First_page_is_selected_on_start()
    {
        var a = new TestPage("A");
        var shell = new ShellViewModel(new PageViewModel[] { a, new TestPage("B") });
        Assert.Same(a, shell.CurrentPage);
        Assert.Equal(2, shell.Pages.Count);
    }

    [Fact]
    public void Navigating_calls_hooks_on_old_and_new_page()
    {
        var a = new TestPage("A");
        var b = new TestPage("B");
        var shell = new ShellViewModel(new PageViewModel[] { a, b });

        shell.CurrentPage = b;

        Assert.Equal(1, a.Left);
        Assert.Equal(1, b.Entered);
    }

    [Fact]
    public void Selecting_same_page_again_does_nothing()
    {
        var a = new TestPage("A");
        var shell = new ShellViewModel(new PageViewModel[] { a });
        var before = a.Entered;
        shell.CurrentPage = a;
        Assert.Equal(before, a.Entered);
        Assert.Equal(0, a.Left);
    }

    [Fact]
    public void Empty_page_list_is_allowed()
    {
        var shell = new ShellViewModel(Array.Empty<PageViewModel>());
        Assert.Null(shell.CurrentPage);
    }
}
