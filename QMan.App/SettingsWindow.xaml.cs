using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using QMan.Core;
using QMan.Data;

namespace QMan.App;

public partial class SettingsWindow : Window
{
    private bool _uiReady;
    private bool _allowClose;
    private Dictionary<string, LlmProviderFormState> _stateByTag = new(StringComparer.OrdinalIgnoreCase);

    public bool FirstRun { get; init; }

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_OnLoaded;
    }

    private void SettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        FirstRunNotice.Visibility = FirstRun ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = FirstRun ? Visibility.Collapsed : Visibility.Visible;

        var ctx = AppContextRoot.Instance;
        var kv = ctx.Settings.LoadAll();
        _stateByTag = AppSettingsDao.LoadProviderFormStates(kv);

        _uiReady = false;
        ProviderCombo.SelectedIndex = ctx.Config.LlmProvider switch
        {
            LlmProvider.Ollama => 1,
            LlmProvider.Claude => 2,
            LlmProvider.GoogleAi => 3,
            LlmProvider.AlibabaCloud => 4,
            _ => 0
        };
        _uiReady = true;

        LoadStateToForm(GetSelectedProviderTag());
        ApplyProviderFields();
    }

    private void ProviderCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is ComboBoxItem oldItem && oldItem.Tag is string oldTag)
            FlushFormToState(oldTag);
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ComboBoxItem newItem && newItem.Tag is string newTag)
            LoadStateToForm(newTag);
        ApplyProviderFields();
    }

    private void FlushFormToState(string tag)
    {
        var s = _stateByTag[tag];
        s.ChatModel = ChatModelBox.Text.Trim();
        s.EmbeddingModel = EmbeddingModelBox.Text.Trim();
        s.Url = UrlBox.Text.Trim();
        s.MainApiKey = ApiKeyBox.Text.Trim();
        s.ClaudeEmbeddingApiKey = EmbeddingApiKeyBox.Text.Trim();
    }

    private void LoadStateToForm(string tag)
    {
        var s = _stateByTag[tag];
        ChatModelBox.Text = s.ChatModel;
        EmbeddingModelBox.Text = s.EmbeddingModel;
        UrlBox.Text = s.Url;
        ApiKeyBox.Text = s.MainApiKey;
        EmbeddingApiKeyBox.Text = s.ClaudeEmbeddingApiKey;
    }

    private string GetSelectedProviderTag()
    {
        if (ProviderCombo.SelectedItem is ComboBoxItem it && it.Tag is string s)
            return s;
        return "openai";
    }

    private void ApplyProviderFields()
    {
        switch (GetSelectedProviderTag())
        {
            case "ollama":
                PanelApiKey.Visibility = Visibility.Collapsed;
                PanelEmbeddingApiKey.Visibility = Visibility.Collapsed;
                PanelUrl.Visibility = Visibility.Visible;
                ChatModelBox.IsEnabled = true;
                EmbeddingModelBox.IsEnabled = true;
                UrlBox.IsEnabled = true;
                break;
            case "openai":
                PanelApiKey.Visibility = Visibility.Visible;
                PanelEmbeddingApiKey.Visibility = Visibility.Collapsed;
                PanelUrl.Visibility = Visibility.Visible;
                ChatModelBox.IsEnabled = true;
                EmbeddingModelBox.IsEnabled = true;
                UrlBox.IsEnabled = true;
                break;
            case "claude":
                PanelApiKey.Visibility = Visibility.Visible;
                PanelEmbeddingApiKey.Visibility = Visibility.Visible;
                PanelUrl.Visibility = Visibility.Collapsed;
                ChatModelBox.IsEnabled = true;
                EmbeddingModelBox.IsEnabled = true;
                UrlBox.IsEnabled = false;
                break;
            case "googleai":
                PanelApiKey.Visibility = Visibility.Visible;
                PanelEmbeddingApiKey.Visibility = Visibility.Collapsed;
                PanelUrl.Visibility = Visibility.Visible;
                ChatModelBox.IsEnabled = true;
                EmbeddingModelBox.IsEnabled = true;
                UrlBox.IsEnabled = true;
                break;
            case "alibabacloud":
                PanelApiKey.Visibility = Visibility.Visible;
                PanelEmbeddingApiKey.Visibility = Visibility.Collapsed;
                PanelUrl.Visibility = Visibility.Visible;
                ChatModelBox.IsEnabled = true;
                EmbeddingModelBox.IsEnabled = true;
                UrlBox.IsEnabled = true;
                break;
        }
    }

    private string? EffectiveMainApiKey(string tag, AppContextRoot ctx)
    {
        var s = _stateByTag[tag].MainApiKey;
        if (!string.IsNullOrWhiteSpace(s))
            return s;
        return ctx.Settings.Get(AppSettingsKeys.ProfileApiKey(tag));
    }

    private string? EffectiveClaudeEmbeddingKey(AppContextRoot ctx)
    {
        var s = _stateByTag["claude"].ClaudeEmbeddingApiKey;
        if (!string.IsNullOrWhiteSpace(s))
            return s;
        return ctx.Settings.Get(AppSettingsKeys.ProfileClaudeEmbeddingApiKey);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        FlushFormToState(GetSelectedProviderTag());
        var tag = GetSelectedProviderTag();
        var ctx = AppContextRoot.Instance;

        var active = _stateByTag[tag];
        if (string.IsNullOrWhiteSpace(active.ChatModel) || string.IsNullOrWhiteSpace(active.EmbeddingModel))
        {
            MessageBox.Show(this, "LLM 모델과 임베딩 모델을 입력해 주세요.", "설정",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (tag is "openai" or "googleai" or "alibabacloud")
        {
            if (string.IsNullOrWhiteSpace(EffectiveMainApiKey(tag, ctx)))
            {
                MessageBox.Show(this, "API 키를 입력해 주세요.", "설정",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (tag == "claude")
        {
            if (string.IsNullOrWhiteSpace(EffectiveMainApiKey(tag, ctx)))
            {
                MessageBox.Show(this, "API 키(Anthropic)를 입력해 주세요.", "설정",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(EffectiveClaudeEmbeddingKey(ctx)))
            {
                MessageBox.Show(this, "임베딩 API 키(OpenAI)를 입력해 주세요.", "설정",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            ctx.Settings.SaveAllProviderProfiles(_stateByTag, tag, markSetupComplete: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _allowClose = true;
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = false;
    }

    private void SettingsWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        if (!FirstRun)
            return;
        e.Cancel = true;
        MessageBox.Show(this, "「저장」으로 설정을 완료해 주세요.", "설정",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
