using VaultDelta.Desktop.ViewModels;

namespace VaultDelta.EndToEnd.Tests.Desktop;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Shell_starts_on_create_package_page()
    {
        MainWindowViewModel viewModel = new();

        Assert.True(viewModel.IsCompareSelected);
        Assert.False(viewModel.IsPatchesSelected);
        Assert.Equal("创建更新包", viewModel.PageTitle);
        Assert.Equal(0, viewModel.SwitchIndicatorOffset);
    }

    [Theory]
    [InlineData("Patches", ShellPage.Patches, "应用更新包")]
    [InlineData("Compare", ShellPage.Compare, "创建更新包")]
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
    public void Advanced_scope_panel_expands_without_changing_the_compare_mode()
    {
        MainWindowViewModel viewModel = new();

        viewModel.ToggleScopePanelCommand.Execute(null);

        Assert.True(viewModel.IsScopePanelExpanded);
        Assert.Equal("收起设置", viewModel.ScopePanelButtonText);
        Assert.True(viewModel.IsCompareSelected);
    }

    [Fact]
    public void Navigation_rejects_unknown_pages()
    {
        MainWindowViewModel viewModel = new();

        Assert.Throws<ArgumentException>(() => viewModel.NavigateCommand.Execute("Unknown"));
    }
}
