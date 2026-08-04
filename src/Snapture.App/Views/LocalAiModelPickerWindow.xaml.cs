using System.Windows;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class LocalAiModelPickerWindow : Window
{
    private readonly IReadOnlyList<LocalAiModelChoice> _choices;

    public LocalAiModelChoice? SelectedChoice { get; private set; }

    public string Prompt => PromptBox.Text.Trim();

    public LocalAiModelPickerWindow(IReadOnlyList<LocalAiProviderInfo> providers)
    {
        InitializeComponent();
        _choices = LocalAiProviderService.GetModelChoices(providers);

        foreach (var choice in _choices)
        {
            ModelCombo.Items.Add(new ComboBoxItem
            {
                Content = choice.DisplayLabel,
                Tag = choice
            });
        }

        var preferred = _choices.FirstOrDefault(choice =>
            ReferenceEquals(
                choice.Model,
                LocalAiProviderService.FindPreferredModel(choice.Provider))) ?? _choices.FirstOrDefault();
        if (preferred is not null)
        {
            var item = ModelCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(comboItem =>
                ReferenceEquals(comboItem.Tag, preferred));
            ModelCombo.SelectedItem = item;
        }
        else
        {
            SendButton.IsEnabled = false;
            StatusText.Text = "No local models are available.";
        }

        UpdateEndpoint();
    }

    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEndpoint();
    }

    private void UpdateEndpoint()
    {
        if (ModelCombo.SelectedItem is ComboBoxItem { Tag: LocalAiModelChoice choice })
        {
            EndpointText.Text = choice.Provider.OpenAiBaseUri is { } endpoint
                ? $"{choice.Reference}\nEndpoint: {endpoint}"
                : choice.Reference;
            StatusText.Text = string.Empty;
        }
    }

    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (ModelCombo.SelectedItem is not ComboBoxItem { Tag: LocalAiModelChoice choice })
        {
            StatusText.Text = "Choose a local model first.";
            return;
        }

        SelectedChoice = choice;
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
