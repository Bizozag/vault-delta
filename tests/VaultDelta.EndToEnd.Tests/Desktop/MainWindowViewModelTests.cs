using VaultDelta.Desktop.ViewModels;

namespace VaultDelta.EndToEnd.Tests.Desktop;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Shell_starts_on_compare_page()
    {
        MainWindowViewModel viewModel = new();

        Assert.True(viewModel.IsCompareSelected);
        Assert.False(viewModel.IsPatchesSelected);
        Assert.Equal("创建增量补丁", viewModel.PageTitle);
    }

    [Theory]
    [InlineData("Patches", ShellPage.Patches, "补丁与恢复")]
    [InlineData("Compare", ShellPage.Compare, "创建增量补丁")]
    public void Navigation_changes_exactly_one_selected_page(
        string parameter,
        ShellPage expectedPage,
        string expectedTitle)
    {
        MainWindowViewModel viewModel = new();

        viewModel.NavigateCommand.Execute(parameter);

        Assert.Equal(expectedPage, viewModel.CurrentPage);
        Assert.Equal(expectedTitle, viewModel.PageTitle);
        Assert.Equal(
            1,
            new[] { viewModel.IsCompareSelected, viewModel.IsPatchesSelected }
                .Count(selected => selected));
    }

    [Fact]
    public void Navigation_rejects_unknown_pages()
    {
        MainWindowViewModel viewModel = new();

        Assert.Throws<ArgumentException>(() => viewModel.NavigateCommand.Execute("Unknown"));
    }
}
